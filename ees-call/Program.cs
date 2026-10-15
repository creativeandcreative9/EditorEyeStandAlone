using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace EesTransportClient;

internal static class Program
{
    private const double DefaultWaitSeconds = 120.0;
    private const int PollIntervalMilliseconds = 50;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonDocumentOptions JsonValidationOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = int.MaxValue
    };
    private static readonly JsonSerializerOptions TraceJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception exception)
        {
            return EmitError(
                4,
                null,
                "unknown",
                null,
                "Unhandled transport client error.",
                exception.Message);
        }
    }

    private static int Run(string[] args)
    {
        if (args.Length == 1 && (args[0] == "--help" || args[0] == "-h"))
        {
            WriteBytesToStandardOutput(Encoding.UTF8.GetBytes(
                "Usage: ees-call --tool <name> [--params-file <UTF-8 JSON path or ->] [--output-file <path>] [--wait-seconds <positive number>]\n" +
                "       ees-call --wait-completion <path> --id <request-id> [--output-file <path>] [--wait-seconds <seconds>] [--fail-on-tool-error]\n" +
                "       ees-call --help\n" +
                "\n" +
                "Send one EES request and print its first response. Place this executable in <Project>/EditorEye/.\n" +
                "\n" +
                "Exit codes: 0=response received (including tool failure by default), 2=invalid input,\n" +
                "3=deadline expired (outcome unknown; do not resend), 4=invalid response/I/O state,\n" +
                "5=outer success=false with --fail-on-tool-error. Inspect tool-specific outcomes.\n" +
                "Acceptance is returned immediately and is not final success.\n\n" +
                "Options:\n" +
                "  --fail-on-tool-error   Return 5 for outer success=false; preserve response JSON.\n" +
                "  --wait-completion <path> Separate read-only finite wait for a done file in this project outbox.\n" +
                "                        Relative paths start at the project root, not the working directory.\n" +
                "  --id <request-id>      Required with --wait-completion; validates response id.\n" +
                "                        Never sends a tool, polls Status, or changes AutoDrive.\n" +
                "                        Default validate_and_apply_scripts needs explicit Status gates.\n" +
                "  --tool <name>          Required EES tool name. Use --tool help for live EES tool documentation.\n" +
                "  --params-file <path|-> UTF-8 JSON parameters file, or '-' to read standard input. Default: {}.\n" +
                "  --output-file <path>   Save exact UTF-8 response bytes to a new file instead of stdout.\n" +
                "                        Relative to working directory; parent must exist; no overwrite.\n" +
                "                        Avoids PowerShell text decoding/BOM; diagnostics remain on stderr.\n" +
                "  --wait-seconds <value> Positive response deadline in seconds. Default: 120.\n" +
                "  -h, --help             Show this local command-line help without contacting Unity.\n"));
            return 0;
        }

        if (!TryParseOptions(args, out CallOptions? options, out string optionError))
        {
            return EmitError(2, null, "not-sent", null, "Invalid command-line input.", optionError);
        }

        CallOptions selectedOptions = options!;
        if (!TryPrepareOutput(selectedOptions.OutputFile, out ResponseOutput? output, out string outputError))
            return EmitError(2, selectedOptions.ExpectedId, "not-sent", null, "Output file could not be prepared. No request was sent.", outputError);
        using ResponseOutput selectedOutput = output!;
        if (selectedOptions.CompletionPath is not null)
        {
            return RunCompletionWait(selectedOptions, selectedOutput);
        }
        if (!TryReadParams(selectedOptions.ParamsFile, out string paramsJson, out string paramsError))
        {
            return EmitError(2, null, "not-sent", null, "Params input is invalid or could not be read.", paramsError);
        }

        string requestId = CreateRequestId();

        if (!TryResolveTransportPaths(out TransportPaths? paths, out string pathError))
        {
            return EmitError(2, requestId, "not-sent", null, "The helper could not locate the project transport folders.", pathError);
        }

        TransportPaths selectedPaths = paths!;
        selectedPaths.CurrentRequestId = requestId;
        if (!TryReadExistingEntries(selectedPaths, requestId, out string collision, out string existingError))
        {
            return EmitError(4, requestId, "unknown", selectedPaths.ResponsePath, "Could not inspect the transport folders.", existingError);
        }

        if (collision.Length != 0)
        {
            return EmitError(2, requestId, "not-sent", selectedPaths.ResponsePath, "Request id collides with an existing transport entry.", collision);
        }

        byte[] requestBytes;
        try
        {
            requestBytes = BuildRequestJson(requestId, selectedOptions.Tool, paramsJson);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            return EmitError(2, requestId, "not-sent", selectedPaths.ResponsePath, "The request JSON could not be constructed.", exception.Message);
        }

        TraceRecord trace = new()
        {
            RequestId = requestId,
            Tool = selectedOptions.Tool,
            InboxPath = selectedPaths.RequestPath,
            ResponsePath = selectedPaths.ResponsePath,
            DonePath = selectedPaths.DonePath,
            State = "prepared",
            CreatedUtc = DateTimeOffset.UtcNow,
            WaitSeconds = selectedOptions.WaitSeconds
        };

        if (!TryCreateTrace(selectedPaths.TracePath, trace, out string traceError))
        {
            return EmitError(2, requestId, "not-sent", selectedPaths.ResponsePath, "Could not save the pre-publication request trace.", traceError);
        }

        PublicationResult publication = PublishRequest(selectedPaths.RequestPath, requestBytes);
        if (!publication.Published)
        {
            trace.State = publication.State;
            trace.Reason = publication.Detail;
            trace.UpdatedUtc = DateTimeOffset.UtcNow;
            TryUpdateTrace(selectedPaths.TracePath, trace);
            return EmitError(publication.ExitCode, requestId, publication.State, selectedPaths.ResponsePath, publication.Reason, publication.Detail);
        }

        trace.State = "published";
        trace.PublishedUtc = DateTimeOffset.UtcNow;
        trace.UpdatedUtc = trace.PublishedUtc;
        TryUpdateTrace(selectedPaths.TracePath, trace);

        ResponseWaitResult response = WaitForFirstResponse(selectedPaths.ResponsePath, requestId, selectedOptions.WaitSeconds);
        switch (response.State)
        {
            case ResponseState.Received:
                trace.State = "response";
                trace.ResponseObservedUtc = DateTimeOffset.UtcNow;
                trace.UpdatedUtc = trace.ResponseObservedUtc;
                TryUpdateTrace(selectedPaths.TracePath, trace);
                try
                {
                    return ReturnResponse(response.Bytes!, selectedOptions.FailOnToolError, false, requestId, selectedPaths.ResponsePath, selectedOutput);
                }
                catch (Exception exception) when (exception is IOException or ObjectDisposedException or UnauthorizedAccessException)
                {
                    trace.State = "unknown";
                    trace.Reason = selectedOptions.OutputFile is null
                        ? "stdout could not be written after the response was received."
                        : $"Response was received but {selectedOutput.Description} could not be saved. Original response remains available; do not resend the tool.";
                    trace.UpdatedUtc = DateTimeOffset.UtcNow;
                    TryUpdateTrace(selectedPaths.TracePath, trace);
                    return EmitError(4, requestId, "unknown", selectedPaths.ResponsePath, trace.Reason, exception.Message);
                }

            case ResponseState.Timeout:
                trace.State = "timeout";
                trace.Reason = response.Detail;
                trace.UpdatedUtc = DateTimeOffset.UtcNow;
                TryUpdateTrace(selectedPaths.TracePath, trace);
                return EmitError(3, requestId, "sent-response-unconfirmed", selectedPaths.ResponsePath, "The response deadline expired after publication.", response.Detail);

            default:
                trace.State = "unknown";
                trace.Reason = response.Detail;
                trace.UpdatedUtc = DateTimeOffset.UtcNow;
                TryUpdateTrace(selectedPaths.TracePath, trace);
                return EmitError(4, requestId, "unknown", selectedPaths.ResponsePath, "The first response was invalid or could not be read.", response.Detail);
        }
    }

    private static int RunCompletionWait(CallOptions options, ResponseOutput output)
    {
        if (!TryResolveTransportPaths(out TransportPaths? paths, out string error))
            return EmitError(2, options.ExpectedId, "not-sent", null, "Could not locate project transport folders.", error);
        string path;
        try
        {
            path = Path.GetFullPath(options.CompletionPath!, paths!.ProjectRoot);
            if (!string.Equals(Path.GetDirectoryName(path), Path.GetFullPath(paths.OutboxPath), StringComparison.OrdinalIgnoreCase) ||
                !path.EndsWith(".done.json", StringComparison.OrdinalIgnoreCase))
                return EmitError(2, options.ExpectedId, "not-sent", path, "Completion must be a .done.json file directly in this project's outbox.", null);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return EmitError(2, options.ExpectedId, "not-sent", null, "Invalid completion path.", exception.Message);
        }
        ResponseWaitResult response = WaitForFirstResponse(path, options.ExpectedId!, options.WaitSeconds);
        if (response.State == ResponseState.Timeout)
            return EmitError(3, options.ExpectedId, "completion-unconfirmed", path, "Completion deadline expired; outcome unknown. No request was sent. Recheck the same path, do not resend the tool.", response.Detail);
        if (response.State != ResponseState.Received)
            return EmitError(4, options.ExpectedId, "completion-unconfirmed", path, "Completion response was invalid or unreadable. No request was sent.", response.Detail);
        try
        {
            return ReturnResponse(response.Bytes!, options.FailOnToolError, true, options.ExpectedId!, path, output);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or UnauthorizedAccessException)
        {
            return EmitError(4, options.ExpectedId, "response-received-output-failed", path,
                $"Completion was received but {output.Description} could not be written. No request was sent; original completion remains available.", exception.Message);
        }
    }

    private static int ReturnResponse(byte[] bytes, bool failOnToolError, bool completion, string requestId, string path, ResponseOutput output)
    {
        if (!failOnToolError && !completion)
        {
            output.Write(bytes);
            return 0;
        }
        using JsonDocument document = JsonDocument.Parse(bytes, JsonValidationOptions);
        JsonElement root = document.RootElement;
        if (!root.TryGetProperty("success", out JsonElement success) ||
            success.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            output.Write(bytes);
            return EmitError(4, requestId, "response-unclassified", path, "Response must contain boolean envelope success for this mode.", null);
        }
        if (completion && (IsIntermediate(root) ||
            (root.TryGetProperty("result", out JsonElement result) && result.ValueKind == JsonValueKind.Object && IsIntermediate(result))))
        {
            output.Write(bytes);
            return EmitError(4, requestId, "completion-unconfirmed", path, "Completion file contains acceptance/intermediate response, not a terminal result.", null);
        }
        output.Write(bytes);
        return failOnToolError && success.ValueKind == JsonValueKind.False ? 5 : 0;
    }

    private static bool IsIntermediate(JsonElement value)
    {
        return (value.TryGetProperty("accepted", out JsonElement accepted) && accepted.ValueKind == JsonValueKind.True) ||
            (value.TryGetProperty("async", out JsonElement asyncFlag) && asyncFlag.ValueKind == JsonValueKind.True) ||
            (value.TryGetProperty("isTerminal", out JsonElement terminal) && terminal.ValueKind == JsonValueKind.False);
    }

    private static bool TryParseOptions(string[] args, out CallOptions? options, out string error)
    {
        options = null;
        error = string.Empty;
        string? tool = null;
        string? paramsFile = null;
        double waitSeconds = DefaultWaitSeconds;
        bool waitSpecified = false;
        bool toolSpecified = false;
        bool paramsSpecified = false;
        bool failOnToolError = false;
        string? completionPath = null;
        string? expectedId = null;
        string? outputFile = null;

        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index] ?? string.Empty;
            string key = argument;
            string? inlineValue = null;
            int equalsIndex = argument.IndexOf('=');
            if (equalsIndex >= 0)
            {
                key = argument[..equalsIndex];
                inlineValue = argument[(equalsIndex + 1)..];
            }

            if (key == "--fail-on-tool-error")
            {
                if (failOnToolError || inlineValue is not null)
                {
                    error = "--fail-on-tool-error is a flag allowed once.";
                    return false;
                }
                failOnToolError = true;
                continue;
            }
            if (key == "--wait-completion" || key == "--id")
            {
                if ((key == "--wait-completion" ? completionPath : expectedId) is not null ||
                    !TryReadOptionValue(args, ref index, inlineValue, out string? value) || string.IsNullOrWhiteSpace(value))
                {
                    error = key + " requires one non-empty value.";
                    return false;
                }
                if (key == "--wait-completion") completionPath = value;
                else expectedId = value;
                continue;
            }

            if (string.Equals(key, "--tool", StringComparison.Ordinal))
            {
                if (toolSpecified || !TryReadOptionValue(args, ref index, inlineValue, out tool) || string.IsNullOrWhiteSpace(tool))
                {
                    error = "--tool must be supplied exactly once with a non-empty value.";
                    return false;
                }

                toolSpecified = true;
                continue;
            }

            if (string.Equals(key, "--params-file", StringComparison.Ordinal))
            {
                if (paramsSpecified || !TryReadOptionValue(args, ref index, inlineValue, out paramsFile) || string.IsNullOrWhiteSpace(paramsFile))
                {
                    error = "--params-file must have one non-empty path, or '-' for UTF-8 stdin.";
                    return false;
                }

                paramsSpecified = true;
                continue;
            }

            if (key == "--output-file")
            {
                if (outputFile is not null || !TryReadOptionValue(args, ref index, inlineValue, out outputFile) || string.IsNullOrWhiteSpace(outputFile))
                {
                    error = "--output-file requires one non-empty path and is allowed once.";
                    return false;
                }
                continue;
            }

            if (string.Equals(key, "--wait-seconds", StringComparison.Ordinal))
            {
                if (waitSpecified || !TryReadOptionValue(args, ref index, inlineValue, out string? waitText) ||
                    !double.TryParse(waitText, NumberStyles.Float, CultureInfo.InvariantCulture, out waitSeconds) ||
                    !double.IsFinite(waitSeconds) || waitSeconds <= 0)
                {
                    error = "--wait-seconds must be a positive finite invariant-culture number.";
                    return false;
                }

                waitSpecified = true;
                continue;
            }

            error = $"Unknown argument '{argument}'.";
            return false;
        }

        if (completionPath is not null ? (toolSpecified || paramsSpecified || expectedId is null) : (!toolSpecified || expectedId is not null))
        {
            error = "Use --tool, or --wait-completion with --id; completion mode forbids --tool and --params-file.";
            return false;
        }

        options = new CallOptions(tool ?? string.Empty, paramsFile, waitSeconds, failOnToolError, completionPath, expectedId, outputFile);
        return true;
    }

    private static bool TryPrepareOutput(string? outputFile, out ResponseOutput? output, out string error)
    {
        output = null;
        error = string.Empty;
        if (outputFile is null)
        {
            output = new ResponseOutput(null, null, null);
            return true;
        }
        try
        {
            if (!TryResolveTransportPaths(out TransportPaths? paths, out error)) return false;
            string path = Path.GetFullPath(outputFile);
            string? parent = Path.GetDirectoryName(path);
            string name = Path.GetFileName(path);
            if (parent is null || !Directory.Exists(parent) || name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new IOException("Output must name a file in an existing directory: " + path);

            // Resolve directory links so a junction cannot target an EES transport folder.
            parent = ResolveDirectoryLinks(parent);
            path = Path.Combine(parent, name);
            foreach (string protectedPath in new[] { paths!.InboxPath, paths.OutboxPath, Path.Combine(paths.ProjectRoot, "EditorEye", "ees-call-traces") })
            {
                string protectedDirectory = ResolveDirectoryLinks(protectedPath).TrimEnd(Path.DirectorySeparatorChar);
                if (string.Equals(parent, protectedDirectory, StringComparison.OrdinalIgnoreCase) ||
                    parent.StartsWith(protectedDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Output cannot be saved in EES inbox, outbox, or ees-call-traces: " + path);
            }
            try
            {
                _ = File.GetAttributes(path);
                throw new IOException("Output already exists; choose a new file: " + path);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }

            string temporary = Path.Combine(parent, ".ees-call-output-" + Guid.NewGuid().ToString("N") + ".tmp");
            var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            output = new ResponseOutput(path, temporary, stream);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            error = $"--output-file '{outputFile}': {exception.Message}";
            return false;
        }
    }

    private static string ResolveDirectoryLinks(string path)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(path));
        string resolved = directory.Parent is null ? directory.FullName : Path.Combine(ResolveDirectoryLinks(directory.Parent.FullName), directory.Name);
        if (Directory.Exists(resolved) && (File.GetAttributes(resolved) & FileAttributes.ReparsePoint) != 0)
            resolved = new DirectoryInfo(resolved).ResolveLinkTarget(returnFinalTarget: true)?.FullName
                ?? throw new IOException("Could not resolve directory link: " + resolved);
        return resolved;
    }

    private static bool TryReadOptionValue(string[] args, ref int index, string? inlineValue, out string? value)
    {
        if (inlineValue is not null)
        {
            value = inlineValue;
            return true;
        }

        if (index + 1 >= args.Length)
        {
            value = null;
            return false;
        }

        string next = args[++index] ?? string.Empty;
        if (next.StartsWith("--", StringComparison.Ordinal))
        {
            value = null;
            return false;
        }

        value = next;
        return true;
    }

    private static bool TryReadParams(string? paramsFile, out string paramsJson, out string error)
    {
        paramsJson = "{}";
        error = string.Empty;
        try
        {
            byte[] bytes = paramsFile is null
                ? Encoding.UTF8.GetBytes("{}")
                : paramsFile == "-"
                    ? ReadAllBytes(Console.OpenStandardInput())
                    : File.ReadAllBytes(Path.GetFullPath(paramsFile));

            string text = DecodeUtf8(bytes);
            if (text.Length > 0 && text[0] == '\ufeff')
            {
                text = text[1..];
            }

            using JsonDocument document = JsonDocument.Parse(text, JsonValidationOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "Params JSON must have an object as its top-level value.";
                return false;
            }

            paramsJson = text;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or DecoderFallbackException or ArgumentException)
        {
            error = exception.Message;
            return false;
        }
    }

    private static byte[] BuildRequestJson(string requestId, string tool, string paramsJson)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Indented = false
        }))
        {
            writer.WriteStartObject();
            writer.WriteString("Id", requestId);
            writer.WriteString("Tool", tool);
            writer.WritePropertyName("Params");
            // Params was already parsed with the explicit unlimited depth above. Keep its
            // original JSON lexemes and do not make Utf8JsonWriter apply its default depth.
            writer.WriteRawValue(paramsJson, skipInputValidation: true);
            writer.WriteEndObject();
            writer.Flush();
        }

        return buffer.ToArray();
    }

    private static bool TryResolveTransportPaths(out TransportPaths? paths, out string error)
    {
        paths = null;
        error = string.Empty;
        try
        {
            string executableDirectory = Path.GetFullPath(AppContext.BaseDirectory);
            DirectoryInfo executableDirectoryInfo = new(executableDirectory);
            if (!string.Equals(executableDirectoryInfo.Name, "EditorEye", StringComparison.OrdinalIgnoreCase))
            {
                error = "The executable is not placed directly under <Project>/EditorEye.";
                return false;
            }

            string? projectRoot = executableDirectoryInfo.Parent?.FullName;
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                error = "The executable's EditorEye parent has no project root.";
                return false;
            }

            string editorEye = Path.Combine(projectRoot, "EditorEye");
            string inbox = Path.Combine(editorEye, "inbox");
            string outbox = Path.Combine(editorEye, "outbox");
            if (!Directory.Exists(inbox) || !Directory.Exists(outbox))
            {
                error = $"Expected existing folders were not found: '{inbox}' and '{outbox}'.";
                return false;
            }

            paths = new TransportPaths(projectRoot, inbox, outbox);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            error = exception.Message;
            return false;
        }
    }

    private static bool TryReadExistingEntries(TransportPaths paths, string requestId, out string collision, out string error)
    {
        collision = string.Empty;
        error = string.Empty;
        try
        {
            string requestPath = Path.Combine(paths.InboxPath, requestId + ".json");
            string responsePath = Path.Combine(paths.OutboxPath, requestId + ".json");
            string donePath = Path.Combine(paths.OutboxPath, requestId + ".done.json");
            if (File.Exists(requestPath))
            {
                collision = $"Existing inbox request: '{requestPath}'.";
            }
            else if (File.Exists(responsePath))
            {
                collision = $"Existing outbox response: '{responsePath}'.";
            }
            else if (File.Exists(donePath))
            {
                collision = $"Existing completion response: '{donePath}'.";
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = exception.Message;
            return false;
        }
    }

    private static PublicationResult PublishRequest(string requestPath, byte[] bytes)
    {
        string temporaryPath = requestPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (FileStream stream = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            TryDeleteUnpublishedTemporaryFile(temporaryPath);
            return PublicationResult.NotPublished(2, "not-sent", "The request could not be written before publication.", exception.Message);
        }

        try
        {
            File.Move(temporaryPath, requestPath, overwrite: false);
            return PublicationResult.PublishedResult();
        }
        catch (IOException exception)
        {
            bool finalExists = File.Exists(requestPath);
            TryDeleteUnpublishedTemporaryFile(temporaryPath);
            return finalExists
                ? PublicationResult.NotPublished(2, "not-sent", "The request id became occupied before atomic publication.", exception.Message)
                : PublicationResult.NotPublished(4, "unknown", "Atomic publication failed and publication state is unknown.", exception.Message);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or ArgumentException)
        {
            TryDeleteUnpublishedTemporaryFile(temporaryPath);
            return PublicationResult.NotPublished(4, "unknown", "Atomic publication failed and publication state is unknown.", exception.Message);
        }
    }

    // ContractTests calls these internal probes without adding a production CLI flag or
    // environment-variable id override. The shipped entry point remains the three-option
    // public contract while the same collision/write implementation is exercised directly.
    internal static bool HasExistingTransportEntryForContractTest(string inboxPath, string outboxPath, string requestId, out string collision)
    {
        var paths = new TransportPaths(Directory.GetParent(inboxPath)?.Parent?.FullName ?? string.Empty, inboxPath, outboxPath)
        {
            CurrentRequestId = requestId
        };
        return TryReadExistingEntries(paths, requestId, out collision, out _);
    }

    internal static int PublishForContractTest(string requestPath, byte[] bytes, out string state)
    {
        PublicationResult result = PublishRequest(requestPath, bytes);
        state = result.State;
        return result.ExitCode;
    }

    private static ResponseWaitResult WaitForFirstResponse(string responsePath, string requestId, double waitSeconds)
    {
        long started = Stopwatch.GetTimestamp();
        bool responseSeen = false;
        string? lastIssue = null;
        while (Stopwatch.GetElapsedTime(started).TotalSeconds < waitSeconds)
        {
            if (File.Exists(responsePath))
            {
                responseSeen = true;
                ResponseReadResult read = TryReadResponse(responsePath, requestId);
                if (read.State == ResponseReadState.Valid)
                {
                    return ResponseWaitResult.Received(read.Bytes!);
                }

                if (read.State == ResponseReadState.Invalid)
                {
                    return ResponseWaitResult.Invalid(read.Detail!);
                }

                lastIssue = read.Detail;
            }

            double remainingSeconds = waitSeconds - Stopwatch.GetElapsedTime(started).TotalSeconds;
            int delay = (int)Math.Clamp(Math.Ceiling(remainingSeconds * 1000), 1, PollIntervalMilliseconds);
            Thread.Sleep(delay);
        }

        if (responseSeen || File.Exists(responsePath))
        {
            return ResponseWaitResult.Invalid(lastIssue ?? "The response file existed but could not be read.");
        }

        return ResponseWaitResult.Timeout($"No response appeared at '{responsePath}'.");
    }

    private static ResponseReadResult TryReadResponse(string responsePath, string requestId)
    {
        byte[] bytes;
        try
        {
            using var stream = new FileStream(responsePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            bytes = buffer.ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ResponseReadResult.Retryable($"The response file could not be read yet: {exception.Message}");
        }

        try
        {
            // JsonDocument validates the JSON grammar but does not guarantee that every
            // raw string byte was decoded through our strict UTF-8 policy. Decode the full
            // payload once before parsing so an invalid byte cannot be returned verbatim.
            _ = StrictUtf8.GetString(bytes);
            using JsonDocument document = JsonDocument.Parse(bytes, JsonValidationOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("id", out JsonElement idElement) ||
                idElement.ValueKind != JsonValueKind.String)
            {
                return ResponseReadResult.Invalid("The response is not a JSON object with a string id.");
            }

            string? responseId = idElement.GetString();
            if (!string.Equals(responseId, requestId, StringComparison.Ordinal))
            {
                return ResponseReadResult.Invalid($"Response id '{responseId}' did not match request id '{requestId}'.");
            }

            return ResponseReadResult.Valid(bytes);
        }
        catch (JsonException exception)
        {
            return ResponseReadResult.Invalid($"The response was not complete valid UTF-8 JSON: {exception.Message}");
        }
        catch (DecoderFallbackException exception)
        {
            return ResponseReadResult.Invalid($"The response was not valid UTF-8: {exception.Message}");
        }
    }

    private static string CreateRequestId()
    {
        long unixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return $"ees-{unixMilliseconds.ToString(CultureInfo.InvariantCulture)}-{Guid.NewGuid():N}";
    }

    private static string DecodeUtf8(byte[] bytes) => StrictUtf8.GetString(bytes);

    private static byte[] ReadAllBytes(Stream stream)
    {
        using (stream)
        {
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }
    }

    private static int EmitError(int exitCode, string? requestId, string submissionState, string? responsePath, string reason, string? detail)
    {
        var diagnostic = new Diagnostic
        {
            Source = "ees-call",
            RequestId = requestId,
            SubmissionState = submissionState,
            ResponsePath = responsePath,
            Reason = reason,
            Detail = detail
        };
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(diagnostic, TraceJsonOptions);
        using Stream stderr = Console.OpenStandardError();
        stderr.Write(bytes, 0, bytes.Length);
        stderr.WriteByte((byte)'\n');
        stderr.Flush();
        return exitCode;
    }

    private static void WriteBytesToStandardOutput(byte[] bytes)
    {
        using Stream stdout = Console.OpenStandardOutput();
        stdout.Write(bytes, 0, bytes.Length);
        stdout.Flush();
    }

    private static bool TryCreateTrace(string tracePath, TraceRecord trace, out string error)
    {
        error = string.Empty;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(tracePath)!);
            WriteTraceAtomically(tracePath, trace, overwrite: false);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            error = exception.Message;
            return false;
        }
    }

    private static void TryUpdateTrace(string tracePath, TraceRecord trace)
    {
        try
        {
            WriteTraceAtomically(tracePath, trace, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            trace.TraceUpdateError = exception.Message;
        }
    }

    private static void WriteTraceAtomically(string tracePath, TraceRecord trace, bool overwrite)
    {
        string temporaryPath = tracePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(trace, TraceJsonOptions);
        try
        {
            using (FileStream stream = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, tracePath, overwrite);
        }
        finally
        {
            TryDeleteUnpublishedTemporaryFile(temporaryPath);
        }
    }

    private static void TryDeleteUnpublishedTemporaryFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record CallOptions(string Tool, string? ParamsFile, double WaitSeconds, bool FailOnToolError, string? CompletionPath, string? ExpectedId, string? OutputFile);

    private sealed class ResponseOutput : IDisposable
    {
        private readonly string? destination;
        private readonly string? temporary;
        private FileStream? stream;

        public ResponseOutput(string? destination, string? temporary, FileStream? stream)
        {
            this.destination = destination;
            this.temporary = temporary;
            this.stream = stream;
        }

        public string Description => destination is null ? "stdout" : $"output file '{destination}'";

        public void Write(byte[] bytes)
        {
            if (destination is null)
            {
                WriteBytesToStandardOutput(bytes);
                return;
            }
            stream!.Write(bytes);
            stream.Flush(flushToDisk: true);
            stream.Dispose();
            stream = null;
            File.Move(temporary!, destination, overwrite: false);
        }

        public void Dispose()
        {
            try { stream?.Dispose(); }
            catch (IOException) { }
            finally
            {
                stream = null;
                if (temporary is not null) TryDeleteUnpublishedTemporaryFile(temporary);
            }
        }
    }

    private sealed class TransportPaths
    {
        public TransportPaths(string projectRoot, string inboxPath, string outboxPath)
        {
            ProjectRoot = projectRoot;
            InboxPath = inboxPath;
            OutboxPath = outboxPath;
        }

        public string ProjectRoot { get; }
        public string InboxPath { get; }
        public string OutboxPath { get; }
        public string RequestPath => Path.Combine(InboxPath, CurrentRequestId! + ".json");
        public string ResponsePath => Path.Combine(OutboxPath, CurrentRequestId! + ".json");
        public string DonePath => Path.Combine(OutboxPath, CurrentRequestId! + ".done.json");
        public string TracePath => Path.Combine(ProjectRoot, "EditorEye", "ees-call-traces", CurrentRequestId! + ".json");
        public string? CurrentRequestId { get; set; }
    }

    private sealed class TraceRecord
    {
        public string RequestId { get; set; } = string.Empty;
        public string Tool { get; set; } = string.Empty;
        public string InboxPath { get; set; } = string.Empty;
        public string ResponsePath { get; set; } = string.Empty;
        public string DonePath { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public double WaitSeconds { get; set; }
        public DateTimeOffset CreatedUtc { get; set; }
        public DateTimeOffset? PublishedUtc { get; set; }
        public DateTimeOffset? ResponseObservedUtc { get; set; }
        public DateTimeOffset? UpdatedUtc { get; set; }
        public string? Reason { get; set; }
        public string? TraceUpdateError { get; set; }
    }

    private sealed class Diagnostic
    {
        public string Source { get; set; } = "ees-call";
        public string? RequestId { get; set; }
        public string SubmissionState { get; set; } = string.Empty;
        public string? ResponsePath { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string? Detail { get; set; }
    }

    private sealed class PublicationResult
    {
        private PublicationResult(bool published, int exitCode, string state, string reason, string? detail)
        {
            Published = published;
            ExitCode = exitCode;
            State = state;
            Reason = reason;
            Detail = detail;
        }

        public bool Published { get; }
        public int ExitCode { get; }
        public string State { get; }
        public string Reason { get; }
        public string? Detail { get; }

        public static PublicationResult PublishedResult() => new(true, 0, "sent", string.Empty, null);
        public static PublicationResult NotPublished(int exitCode, string state, string reason, string? detail) => new(false, exitCode, state, reason, detail);
    }

    private enum ResponseState
    {
        Received,
        Timeout,
        Invalid
    }

    private sealed class ResponseWaitResult
    {
        private ResponseWaitResult(ResponseState state, byte[]? bytes, string? detail)
        {
            State = state;
            Bytes = bytes;
            Detail = detail;
        }

        public ResponseState State { get; }
        public byte[]? Bytes { get; }
        public string? Detail { get; }

        public static ResponseWaitResult Received(byte[] bytes) => new(ResponseState.Received, bytes, null);
        public static ResponseWaitResult Timeout(string detail) => new(ResponseState.Timeout, null, detail);
        public static ResponseWaitResult Invalid(string detail) => new(ResponseState.Invalid, null, detail);
    }

    private enum ResponseReadState
    {
        Valid,
        Retryable,
        Invalid
    }

    private sealed class ResponseReadResult
    {
        private ResponseReadResult(ResponseReadState state, byte[]? bytes, string? detail)
        {
            State = state;
            Bytes = bytes;
            Detail = detail;
        }

        public ResponseReadState State { get; }
        public byte[]? Bytes { get; }
        public string? Detail { get; }

        public static ResponseReadResult Valid(byte[] bytes) => new(ResponseReadState.Valid, bytes, null);
        public static ResponseReadResult Retryable(string detail) => new(ResponseReadState.Retryable, null, detail);
        public static ResponseReadResult Invalid(string detail) => new(ResponseReadState.Invalid, null, detail);
    }
}
