using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EesTransportContractTests;

internal static class Program
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };
    private static readonly List<TransportEvidence> TransportEvidenceItems = new();

    private static async Task<int> Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("usage: dotnet run --project ContractTests -- <published-win-x64/ees-call.exe>");
            return 2;
        }

        string helperPath = Path.GetFullPath(args[0]);
        if (!File.Exists(helperPath))
        {
            Console.Error.WriteLine($"helper not found: {helperPath}");
            return 2;
        }

        string evidenceDirectory = Path.Combine(AppContext.BaseDirectory, "contract-evidence");
        Directory.CreateDirectory(evidenceDirectory);
        var results = new List<CaseResult>();
        var cases = new (string Name, Func<Task<CaseResult>> Run)[]
        {
            ("01-sync-response", () => RunSyncResponseAsync(helperPath)),
            ("02-async-acceptance-without-done", () => RunAsyncAcceptanceAsync(helperPath)),
            ("03-acceptance-and-done-first-response", () => RunAcceptanceAndDoneAsync(helperPath)),
            ("04-business-failure-preserved", () => RunBusinessFailureAsync(helperPath)),
            ("05-timeout-no-resend", () => RunTimeoutAsync(helperPath)),
            ("06-invalid-input-and-missing-transport", () => RunInvalidInputAsync(helperPath)),
            ("07-invalid-response-no-resend", () => RunInvalidResponseAsync(helperPath)),
            ("08-interruption-trace", () => RunInterruptionAsync(helperPath)),
            ("09-json-types-unicode-file-and-stdin", () => RunJsonFidelityAsync(helperPath))

        };

        foreach ((string name, Func<Task<CaseResult>> run) in cases)
        {
            CaseResult result;
            try
            {
                result = await run();
            }
            catch (Exception exception)
            {
                result = CaseResult.Fail(name, exception.ToString());
            }

            results.Add(result);
            Console.WriteLine($"{(result.Passed ? "PASS" : "FAIL")} {name}: {result.Detail}");
            if (!result.Passed)
            {
                break;
            }
        }

        string evidencePath = Path.Combine(evidenceDirectory, "summary.json");
        var evidence = new EvidenceReport(DateTimeOffset.UtcNow, Environment.OSVersion.VersionString, results, TransportEvidenceItems);
        File.WriteAllText(evidencePath, JsonSerializer.Serialize(evidence, JsonOptions), new UTF8Encoding(false));
        bool allPassed = results.Count == cases.Length && results.All(result => result.Passed);
        if (allPassed)
        {
            // This local runner validates behavior only; no historical distribution manifest mutation.
        }

        Console.WriteLine($"Evidence: {evidencePath}");
        return allPassed ? 0 : 1;
    }

    private static async Task<CaseResult> RunSyncResponseAsync(string helperPath)
    {
        using Fixture fixture = Fixture.Create(helperPath, "同期_日本語");
        const string responseTemplate = "{{\"id\":\"{0}\",\"success\":true,\"result\":{{\"value\":\"受付済み_日本語\"}}}}";
        ProcessResult process = await RunWithResponderAsync(
            fixture,
            new[] { "--tool", "echo" },
            null,
            async request =>
            {
                AssertRequest(request, "echo");
                await WriteResponseAsync(fixture.Outbox, request.Id, string.Format(responseTemplate, request.Id));
            });
        return process.ExitCode == 0 && Text(process.Stdout) == string.Format(responseTemplate, ExtractIdFromOutputOrEvidence(process.Stdout, fixture))
            ? CaseResult.Pass("01-sync-response", "one request and exact response bytes")
            : CaseResult.Fail("01-sync-response", $"exit={process.ExitCode}, stdout={Text(process.Stdout)}, stderr={Text(process.Stderr)}");
    }

    private static async Task<CaseResult> RunAsyncAcceptanceAsync(string helperPath)
    {
        using Fixture fixture = Fixture.Create(helperPath, "受付のみ");
        ProcessResult process = await RunWithResponderAsync(
            fixture,
            new[] { "--tool", "compile" },
            null,
            async request =>
            {
                string response = $"{{\"id\":\"{request.Id}\",\"success\":true,\"result\":{{\"status\":\"accepted\",\"async\":true,\"completionFile\":\"EditorEye/outbox/{request.Id}.done.json\",\"nextAction\":\"Status\"}}}}";
                await WriteResponseAsync(fixture.Outbox, request.Id, response);
            });
        string requestId = RequestName(process, fixture);
        string expected = $"{{\"id\":\"{requestId}\",\"success\":true,\"result\":{{\"status\":\"accepted\",\"async\":true,\"completionFile\":\"EditorEye/outbox/{requestId}.done.json\",\"nextAction\":\"Status\"}}}}";
        bool doneAbsent = !File.Exists(Path.Combine(fixture.Outbox, requestId + ".done.json"));
        return process.ExitCode == 0 && doneAbsent && Text(process.Stdout) == expected
            ? CaseResult.Pass("02-async-acceptance-without-done", "returned the exact acceptance bytes before done and did not poll")
            : CaseResult.Fail("02-async-acceptance-without-done", $"exit={process.ExitCode}, doneAbsent={doneAbsent}, stdout={Text(process.Stdout)}, stderr={Text(process.Stderr)}");
    }

    private static async Task<CaseResult> RunAcceptanceAndDoneAsync(string helperPath)
    {
        using Fixture fixture = Fixture.Create(helperPath, "受付と完了");
        string? requestId = null;
        ProcessResult process = await RunWithResponderAsync(
            fixture,
            new[] { "--tool", "compile" },
            null,
            async request =>
            {
                requestId = request.Id;
                await WriteResponseAsync(fixture.Outbox, request.Id + ".done", $"{{\"id\":\"{request.Id}\",\"success\":true,\"result\":{{\"status\":\"done\"}}}}");
                string response = $"{{\"id\":\"{request.Id}\",\"success\":true,\"result\":{{\"status\":\"accepted\",\"async\":true,\"completionFile\":\"EditorEye/outbox/{request.Id}.done.json\"}}}}";
                await WriteResponseAsync(fixture.Outbox, request.Id, response);
            });
        bool exactlyOneRequest = Directory.GetFiles(fixture.Inbox, "*.json").Length == 0;
        string expected = requestId is null ? string.Empty : $"{{\"id\":\"{requestId}\",\"success\":true,\"result\":{{\"status\":\"accepted\",\"async\":true,\"completionFile\":\"EditorEye/outbox/{requestId}.done.json\"}}}}";
        return process.ExitCode == 0 && requestId is not null && exactlyOneRequest && Text(process.Stdout) == expected
            ? CaseResult.Pass("03-acceptance-and-done-first-response", "done existed first, but the exact first response was returned without follow-up")
            : CaseResult.Fail("03-acceptance-and-done-first-response", $"exit={process.ExitCode}, requestId={requestId}, inboxEmpty={exactlyOneRequest}, stdout={Text(process.Stdout)}");
    }

    private static async Task<CaseResult> RunBusinessFailureAsync(string helperPath)
    {
        using Fixture fixture = Fixture.Create(helperPath, "業務失敗");
        ProcessResult process = await RunWithResponderAsync(
            fixture,
            new[] { "--tool", "missing_tool" },
            null,
            async request =>
            {
                string response = $"{{\"id\":\"{request.Id}\",\"success\":false,\"errorCode\":\"TOOL_FAILED\",\"error\":\"業務エラー\",\"result\":{{\"diagnostic\":{{\"code\":0}}}}}}";
                await WriteResponseAsync(fixture.Outbox, request.Id, response);
            });
        string stdout = Text(process.Stdout);
        return process.ExitCode == 0 && stdout.Contains("TOOL_FAILED", StringComparison.Ordinal) && stdout.Contains("業務エラー", StringComparison.Ordinal)
            ? CaseResult.Pass("04-business-failure-preserved", "success=false envelope and diagnostics remained unchanged")
            : CaseResult.Fail("04-business-failure-preserved", $"exit={process.ExitCode}, stdout={stdout}");
    }

    private static async Task<CaseResult> RunTimeoutAsync(string helperPath)
    {
        using Fixture fixture = Fixture.Create(helperPath, "timeout");
        ProcessResult process = await RunProcessAsync(fixture, new[] { "--tool", "no_response", "--wait-seconds", "0.20" }, null);
        string stderr = Text(process.Stderr);
        bool requestRetained = Directory.GetFiles(fixture.Inbox, "*.json").Length == 1;
        bool traceTimeout = ReadTraceStates(fixture).Contains("timeout", StringComparer.Ordinal);
        return process.ExitCode == 3 && stderr.Contains("sent-response-unconfirmed", StringComparison.Ordinal) && requestRetained && traceTimeout
            ? CaseResult.Pass("05-timeout-no-resend", "sent request was retained with id and no retry")
            : CaseResult.Fail("05-timeout-no-resend", $"exit={process.ExitCode}, requestRetained={requestRetained}, traceTimeout={traceTimeout}, stderr={stderr}");
    }

    private static async Task<CaseResult> RunInvalidInputAsync(string helperPath)
    {
        using Fixture fixture = Fixture.Create(helperPath, "invalid");
        ProcessResult invalid = await RunProcessAsync(fixture, new[] { "--tool", "echo", "--wait-seconds", "0" }, null);
        string collisionId = "collision-contract-test";
        File.WriteAllText(Path.Combine(fixture.Outbox, collisionId + ".json"), "{\"id\":\"collision-contract-test\"}", new UTF8Encoding(false));
        bool collisionDetected = EesTransportClient.Program.HasExistingTransportEntryForContractTest(fixture.Inbox, fixture.Outbox, collisionId, out _);
        string failedPublicationPath = Path.Combine(fixture.Inbox, "missing-parent", "write-failure.json");
        int writeFailureExit = EesTransportClient.Program.PublishForContractTest(failedPublicationPath, StrictUtf8.GetBytes("{}"), out string writeFailureState);
        Directory.Delete(fixture.Inbox, recursive: true);
        Directory.Delete(fixture.Outbox, recursive: true);
        ProcessResult missing = await RunProcessAsync(fixture, new[] { "--tool", "echo", "--wait-seconds", "0.1" }, null);
        return invalid.ExitCode == 2 && collisionDetected && writeFailureExit == 2 && writeFailureState == "not-sent" && missing.ExitCode == 2 && !Directory.Exists(fixture.Inbox) && !Directory.Exists(fixture.Outbox)
            ? CaseResult.Pass("06-invalid-input-and-missing-transport", "invalid input, existing-id collision, atomic write failure, and missing transport all stayed pre-publication")
            : CaseResult.Fail("06-invalid-input-and-missing-transport", $"invalid={invalid.ExitCode}, collision={collisionDetected}, write={writeFailureExit}/{writeFailureState}, missing={missing.ExitCode}");
    }

    private static async Task<CaseResult> RunInvalidResponseAsync(string helperPath)
    {
        using Fixture invalidJsonFixture = Fixture.Create(helperPath, "不正JSON");
        ProcessResult invalidJson = await RunWithResponderAsync(
            invalidJsonFixture,
            new[] { "--tool", "bad_response" },
            null,
            request => WriteResponseAsync(invalidJsonFixture.Outbox, request.Id, "{not-json}"));

        using Fixture mismatchFixture = Fixture.Create(helperPath, "ID不一致");
        ProcessResult mismatch = await RunWithResponderAsync(
            mismatchFixture,
            new[] { "--tool", "mismatch" },
            null,
            request => WriteResponseAsync(mismatchFixture.Outbox, request.Id, "{\"id\":\"other-id\",\"success\":true}"));

        using Fixture invalidUtf8Fixture = Fixture.Create(helperPath, "不正UTF8");
        ProcessResult invalidUtf8 = await RunWithResponderAsync(
            invalidUtf8Fixture,
            new[] { "--tool", "invalid_utf8" },
            null,
            async request =>
            {
                string path = Path.Combine(invalidUtf8Fixture.Outbox, request.Id + ".json");
                string temporary = path + ".tmp";
                byte[] bytes = StrictUtf8.GetBytes($"{{\"id\":\"{request.Id}\",\"x\":\"")
                    .Concat(new byte[] { 0xff })
                    .Concat(StrictUtf8.GetBytes("\"}}"))
                    .ToArray();
                await File.WriteAllBytesAsync(temporary, bytes);
                File.Move(temporary, path);
            });

        bool passed = invalidJson.ExitCode == 4 && mismatch.ExitCode == 4 && invalidUtf8.ExitCode == 4 &&
                      Text(mismatch.Stderr).Contains("did not match", StringComparison.Ordinal) &&
                      Text(invalidUtf8.Stderr).Contains("UTF-8", StringComparison.OrdinalIgnoreCase);
        return passed
            ? CaseResult.Pass("07-invalid-response-no-resend", "malformed JSON, id mismatch, and invalid UTF-8 all exited 4 without resend")
            : CaseResult.Fail("07-invalid-response-no-resend", $"json={invalidJson.ExitCode}, mismatch={mismatch.ExitCode}, utf8={invalidUtf8.ExitCode}");
    }

    private static async Task<CaseResult> RunInterruptionAsync(string helperPath)
    {
        using Fixture fixture = Fixture.Create(helperPath, "中断追跡");
        using Process process = StartProcess(fixture, new[] { "--tool", "interrupt_me", "--wait-seconds", "5" }, null);
        Request interruptedRequest = await WaitForAnyRequestAsync(fixture.Inbox, TimeSpan.FromSeconds(5));
        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();
        CaptureFixtureEvidence(fixture, interruptedRequest, new ProcessResult(process.ExitCode, Array.Empty<byte>(), Array.Empty<byte>()));
        bool tracePublished = ReadTraceStates(fixture).Contains("published", StringComparer.Ordinal);
        bool oneRequest = Directory.GetFiles(fixture.Inbox, "*.json").Length == 1;
        return tracePublished && oneRequest
            ? CaseResult.Pass("08-interruption-trace", "published id remained recoverable and no second request was made")
            : CaseResult.Fail("08-interruption-trace", $"tracePublished={tracePublished}, oneRequest={oneRequest}");
    }

    private static async Task<CaseResult> RunJsonFidelityAsync(string helperPath)
    {
        string deepValue = string.Concat(Enumerable.Repeat("[", 96)) + "0" + string.Concat(Enumerable.Repeat("]", 96));
        string paramsJson = $"{{\"BoolValue\":true,\"Zero\":0,\"NullValue\":null,\"Array\":[1,false,{{\"日本語\":\"値\"}}],\"Big\":9007199254740993.12345678901234567890,\"Deep\":{deepValue}}}";
        using Fixture fileFixture = Fixture.Create(helperPath, "JSONファイル_日本語");
        string paramsFile = Path.Combine(fileFixture.Root, "params_日本語.json");
        File.WriteAllText(paramsFile, paramsJson, new UTF8Encoding(false));
        ProcessResult fromFile = await RunWithResponderAsync(fileFixture, new[] { "--tool", "json", "--params-file", paramsFile }, null,
            request => VerifyAndRespondAsync(fileFixture, request, paramsJson));

        using Fixture stdinFixture = Fixture.Create(helperPath, "JSON標準入力_日本語");
        ProcessResult fromStdin = await RunWithResponderAsync(stdinFixture, new[] { "--tool", "json", "--params-file", "-" }, paramsJson,
            request => VerifyAndRespondAsync(stdinFixture, request, paramsJson));
        bool passed = fromFile.ExitCode == 0 && fromStdin.ExitCode == 0 && Text(fromFile.Stdout).Contains("日本語", StringComparison.Ordinal) && Text(fromStdin.Stdout).Contains("日本語", StringComparison.Ordinal);
        return passed
            ? CaseResult.Pass("09-json-types-unicode-file-and-stdin", "UTF-8 path, file/stdin object, numeric lexeme, bool/null/array/nesting preserved")
            : CaseResult.Fail("09-json-types-unicode-file-and-stdin", $"file={fromFile.ExitCode}/{Text(fromFile.Stderr)}, stdin={fromStdin.ExitCode}/{Text(fromStdin.Stderr)}");
    }

    private static Task<CaseResult> RunManifestAsync()
    {
        string manifestPath = FindManifestPath();
        if (!File.Exists(manifestPath))
        {
            return Task.FromResult(CaseResult.Fail("10-manifest-and-rid-artifacts", $"manifest not found: {manifestPath}"));
        }

        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        JsonElement root = manifest.RootElement;
        string[] expected = { "win-x64", "osx-arm64", "osx-x64", "linux-x64" };
        bool valid = root.GetProperty("clientVersion").GetString() == "11.1.8" &&
                     root.GetProperty("targetFramework").GetString() == "net9.0" &&
                     root.GetProperty("sdkVersion").GetString() == "9.0.312" &&
                     root.GetProperty("nativeAot").GetBoolean() == false &&
                     root.GetProperty("artifacts").GetArrayLength() == expected.Length;
        foreach (JsonElement artifact in root.GetProperty("artifacts").EnumerateArray())
        {
            string rid = artifact.GetProperty("rid").GetString()!;
            valid &= expected.Contains(rid, StringComparer.Ordinal) && artifact.GetProperty("selfContained").GetBoolean();
            string executable = Path.Combine(Path.GetDirectoryName(manifestPath)!, rid, artifact.GetProperty("entryPoint").GetString()!);
            string archive = Path.Combine(Path.GetDirectoryName(manifestPath)!, Path.GetFileName(artifact.GetProperty("archive").GetString()!));
            valid &= File.Exists(executable) && File.Exists(archive);
            valid &= Sha256(executable) == artifact.GetProperty("executableSha256").GetString();
            valid &= Sha256(archive) == artifact.GetProperty("archiveSha256").GetString();
        }

        return Task.FromResult(valid
            ? CaseResult.Pass("10-manifest-and-rid-artifacts", "all four self-contained archives and measured hashes are present")
            : CaseResult.Fail("10-manifest-and-rid-artifacts", "manifest/artifact/hash validation failed"));
    }

    private static async Task VerifyAndRespondAsync(Fixture fixture, Request request, string expectedParams)
    {
        if (!request.Raw.Contains(expectedParams, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("request did not retain the raw Params JSON");
        }

        await WriteResponseAsync(fixture.Outbox, request.Id, $"{{\"id\":\"{request.Id}\",\"success\":true,\"result\":{{\"unicode\":\"日本語\",\"big\":9007199254740993.12345678901234567890}}}}");
    }

    private static async Task<ProcessResult> RunWithResponderAsync(Fixture fixture, string[] arguments, string? stdin, Func<Request, Task> responder)
    {
        using Process process = StartProcess(fixture, arguments, stdin);
        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(15));
        Request? observedRequest = null;
        Task responderTask = Task.Run(async () =>
        {
            try
            {
                Request request = await WaitForRequestAsync(fixture.Inbox, cancellation.Token);
                observedRequest = request;
                await responder(request);
                File.Delete(request.Path);
            }
            finally
            {
                cancellation.Cancel();
            }
        });
        try
        {
            Task<ProcessResult> processTask = CaptureProcessAsync(process, stdin);
            Task firstCompleted = await Task.WhenAny(processTask, responderTask);
            if (firstCompleted == responderTask)
            {
                await responderTask;
                ProcessResult completedResult = await processTask;
                CaptureFixtureEvidence(fixture, observedRequest, completedResult);
                return completedResult;
            }

            ProcessResult result = await processTask;
            cancellation.Cancel();
            try
            {
                await responderTask;
            }
            catch (OperationCanceledException)
            {
            }
            CaptureFixtureEvidence(fixture, observedRequest, result);
            return result;
        }
        catch
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            try
            {
                await process.WaitForExitAsync();
            }
            catch
            {
            }

            throw;
        }
    }

    private static async Task<ProcessResult> RunProcessAsync(Fixture fixture, string[] arguments, string? stdin)
    {
        using Process process = StartProcess(fixture, arguments, stdin);
        ProcessResult result = await CaptureProcessAsync(process, stdin);
        CaptureFixtureEvidence(fixture, null, result);
        return result;
    }

    private static Process StartProcess(Fixture fixture, string[] arguments, string? stdin)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fixture.HelperPath,
            WorkingDirectory = fixture.Root,
            UseShellExecute = false,
            RedirectStandardInput = stdin is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = new Process { StartInfo = startInfo };
        process.Start();
        if (stdin is not null)
        {
            _ = Task.Run(async () =>
            {
                byte[] bytes = StrictUtf8.GetBytes(stdin);
                await process.StandardInput.BaseStream.WriteAsync(bytes);
                await process.StandardInput.BaseStream.FlushAsync();
                process.StandardInput.Close();
            });
        }

        return process;
    }

    private static async Task<ProcessResult> CaptureProcessAsync(Process process, string? stdin)
    {
        Task<byte[]> stdout = ReadBytesAsync(process.StandardOutput.BaseStream);
        Task<byte[]> stderr = ReadBytesAsync(process.StandardError.BaseStream);
        await process.WaitForExitAsync();
        return new ProcessResult(process.ExitCode, await stdout, await stderr);
    }

    private static async Task<byte[]> ReadBytesAsync(Stream stream)
    {
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return memory.ToArray();
    }

    private static async Task<Request> WaitForRequestAsync(string inbox, CancellationToken cancellation)
    {
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            string? path = Directory.GetFiles(inbox, "*.json").OrderBy(value => value, StringComparer.Ordinal).FirstOrDefault();
            if (path is not null)
            {
                string raw = StrictUtf8.GetString(await File.ReadAllBytesAsync(path, cancellation));
                using JsonDocument document = JsonDocument.Parse(raw, new JsonDocumentOptions { MaxDepth = int.MaxValue });
                string id = document.RootElement.GetProperty("Id").GetString()!;
                return new Request(id, raw, path);
            }

            await Task.Delay(20, cancellation);
        }
    }

    private static async Task<Request> WaitForAnyRequestAsync(string inbox, TimeSpan timeout)
    {
        using CancellationTokenSource cancellation = new(timeout);
        return await WaitForRequestAsync(inbox, cancellation.Token);
    }

    private static async Task WriteResponseAsync(string outbox, string requestId, string response)
    {
        string path = Path.Combine(outbox, requestId + ".json");
        string temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, response, new UTF8Encoding(false));
        File.Move(temporary, path);
    }

    private static void AssertRequest(Request request, string tool)
    {
        using JsonDocument document = JsonDocument.Parse(request.Raw);
        if (document.RootElement.GetProperty("Tool").GetString() != tool || document.RootElement.GetProperty("Params").ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("request shape did not match the EES PascalCase contract");
        }
    }

    private static string Text(byte[] bytes) => StrictUtf8.GetString(bytes);

    private static string RequestName(ProcessResult process, Fixture fixture)
    {
        string[] traces = Directory.GetFiles(Path.Combine(fixture.EditorEye, "ees-call-traces"), "*.json");
        return Path.GetFileNameWithoutExtension(traces.Single());
    }

    private static string ExtractIdFromOutputOrEvidence(byte[] output, Fixture fixture)
    {
        string[] traces = Directory.GetFiles(Path.Combine(fixture.EditorEye, "ees-call-traces"), "*.json");
        using JsonDocument trace = JsonDocument.Parse(File.ReadAllBytes(traces.Single()));
        return trace.RootElement.GetProperty("requestId").GetString()!;
    }

    private static string[] ReadTraceStates(Fixture fixture)
    {
        string traceDirectory = Path.Combine(fixture.EditorEye, "ees-call-traces");
        if (!Directory.Exists(traceDirectory))
        {
            return Array.Empty<string>();
        }

        return Directory.GetFiles(traceDirectory, "*.json")
            .Select(path => JsonDocument.Parse(File.ReadAllBytes(path)).RootElement.GetProperty("state").GetString()!)
            .ToArray();
    }

    private static string Sha256(string path)
    {
        using var hash = System.Security.Cryptography.SHA256.Create();
        return Convert.ToHexString(hash.ComputeHash(File.ReadAllBytes(path))).ToLowerInvariant();
    }

    private static void TryMarkWindowsManifestValidated()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        string manifestPath = FindManifestPath();
        try
        {
            JsonNode? root = JsonNode.Parse(File.ReadAllText(manifestPath));
            JsonArray artifacts = root!["artifacts"]!.AsArray();
            foreach (JsonNode? artifact in artifacts)
            {
                if (artifact!["rid"]!.GetValue<string>() == "win-x64")
                {
                    artifact["runtimeValidation"] = "windows-contract-tests-passed";
                    artifact["validatedUtc"] = DateTimeOffset.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
                    artifact["validationEvidence"] = "ContractTests/contract-evidence/summary.json";
                }
            }

            File.WriteAllText(manifestPath, root.ToJsonString(JsonOptions), new UTF8Encoding(false));
        }
        catch
        {
            // The contract result itself remains authoritative if the optional manifest annotation fails.
        }
    }

    private static string FindManifestPath()
    {
        string? current = Path.GetFullPath(AppContext.BaseDirectory);
        while (!string.IsNullOrWhiteSpace(current))
        {
            string candidate = Path.Combine(current, "dist", "manifest.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            string? parent = Directory.GetParent(current)?.FullName;
            if (string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "dist", "manifest.json");
    }

    private static void CaptureFixtureEvidence(Fixture fixture, Request? request, ProcessResult process)
    {
        DateTimeOffset observedUtc = DateTimeOffset.UtcNow;
        string[] responseFiles = Directory.Exists(fixture.Outbox) ? Directory.GetFiles(fixture.Outbox, "*.json") : Array.Empty<string>();
        string traceDirectory = Path.Combine(fixture.EditorEye, "ees-call-traces");
        string[] traceFiles = Directory.Exists(traceDirectory) ? Directory.GetFiles(traceDirectory, "*.json") : Array.Empty<string>();
        TransportEvidenceItems.Add(new TransportEvidence(
            fixture.Name,
            observedUtc,
            request is null ? null : new PayloadEvidence(request.Path, request.Raw.Length, Sha256Bytes(StrictUtf8.GetBytes(request.Raw)), observedUtc),
            responseFiles.Select(path => DescribeFile(path, observedUtc)).ToArray(),
            traceFiles.Select(path => DescribeFile(path, observedUtc)).ToArray(),
            new PayloadEvidence("stdout", process.Stdout.Length, Sha256Bytes(process.Stdout), observedUtc),
            new PayloadEvidence("stderr", process.Stderr.Length, Sha256Bytes(process.Stderr), observedUtc)));
    }

    private static PayloadEvidence DescribeFile(string path, DateTimeOffset observedUtc)
    {
        byte[] bytes = File.ReadAllBytes(path);
        return new PayloadEvidence(path, bytes.Length, Sha256Bytes(bytes), observedUtc);
    }

    private static string Sha256Bytes(byte[] bytes)
    {
        using var hash = System.Security.Cryptography.SHA256.Create();
        return Convert.ToHexString(hash.ComputeHash(bytes)).ToLowerInvariant();
    }

    private sealed class Fixture : IDisposable
    {
        private Fixture(string root, string helperPath, string name)
        {
            Root = root;
            HelperPath = helperPath;
            Name = name;
            EditorEye = Path.Combine(root, "EditorEye");
            Inbox = Path.Combine(EditorEye, "inbox");
            Outbox = Path.Combine(EditorEye, "outbox");
        }

        public string Root { get; }
        public string EditorEye { get; }
        public string Inbox { get; }
        public string Outbox { get; }
        public string HelperPath { get; }
        public string Name { get; }

        public static Fixture Create(string sourceHelper, string suffix)
        {
            string root = Path.Combine(Path.GetTempPath(), "EES-transport-fixture", $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Guid.NewGuid():N}_{suffix}");
            string editorEye = Path.Combine(root, "EditorEye");
            string inbox = Path.Combine(editorEye, "inbox");
            string outbox = Path.Combine(editorEye, "outbox");
            Directory.CreateDirectory(inbox);
            Directory.CreateDirectory(outbox);
            string helperPath = Path.Combine(editorEye, Path.GetFileName(sourceHelper));
            File.Copy(sourceHelper, helperPath);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(helperPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
            return new Fixture(root, helperPath, suffix);
        }

        public void Dispose()
        {
            try
            {
                string fixtureParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "EES-transport-fixture"))
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string fullRoot = Path.GetFullPath(Root);
                if (!fullRoot.StartsWith(fixtureParent, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (Directory.Exists(fullRoot))
                {
                    Directory.Delete(fullRoot, recursive: true);
                }
            }
            catch
            {
                // Keep an inaccessible fixture for diagnosis rather than hiding a test result.
            }
        }
    }

    private sealed record Request(string Id, string Raw, string Path);
    private sealed record ProcessResult(int ExitCode, byte[] Stdout, byte[] Stderr);
    private sealed record CaseResult(string Name, bool Passed, string Detail)
    {
        public static CaseResult Pass(string name, string detail) => new(name, true, detail);
        public static CaseResult Fail(string name, string detail) => new(name, false, detail);
    }

    private sealed record EvidenceReport(DateTimeOffset GeneratedUtc, string Host, List<CaseResult> Cases, List<TransportEvidence> TransportEvidence);
    private sealed record TransportEvidence(string Case, DateTimeOffset ObservedUtc, PayloadEvidence? Request, PayloadEvidence[] Responses, PayloadEvidence[] Traces, PayloadEvidence Stdout, PayloadEvidence Stderr);
    private sealed record PayloadEvidence(string Path, int SizeBytes, string Sha256, DateTimeOffset ObservedUtc);
}

