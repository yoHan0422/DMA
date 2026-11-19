using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DmaLauncher;

internal static class Program
{
    [STAThread]
    public static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new LauncherForm());
    }
}

internal sealed record ReleasePackage(Version Version, string Tag, string ZipUrl, string? HashUrl);

internal sealed class LauncherForm : Form
{
    private const string Repo = "yoHan0422/DMA";
    private const string ReleaseApi = "https://api.github.com/repos/" + Repo + "/releases?per_page=100";
    private static readonly string DataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DMA", "Launcher");
    private static readonly string VersionsRoot = Path.Combine(DataRoot, "versions");
    private static readonly string LogPath = Path.Combine(DataRoot, "launcher.log");
    private static readonly HttpClient Http = MakeHttpClient();

    private readonly Label _header = new() { Text = "DMA LAUNCHER", Font = new Font("Malgun Gothic", 17, FontStyle.Bold), AutoSize = true, ForeColor = Color.FromArgb(24, 57, 110) };
    private readonly Label _sub = new() { Text = "게임 도우미 · 설치 및 자동 업데이트", Font = new Font("Malgun Gothic", 10), AutoSize = true };
    private readonly Label _status = new() { Text = "실행을 눌러 DMA를 시작하세요.", Font = new Font("Malgun Gothic", 9), AutoSize = false, Height = 45, Width = 520 };
    private readonly Label _version = new() { Text = "설치된 DMA: 확인 전", Font = new Font("Malgun Gothic", 9), AutoSize = true };
    private readonly ProgressBar _progress = new() { Width = 520, Height = 18, Style = ProgressBarStyle.Continuous };
    private readonly TextBox _logBox = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 8.6f), BackColor = Color.FromArgb(246, 248, 252), Width = 520, Height = 133 };
    private readonly Button _start = new() { Text = "실행", Width = 160, Height = 36, BackColor = Color.FromArgb(28, 76, 146), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
    private readonly Button _stop = new() { Text = "복구 중지", Width = 160, Height = 36, Enabled = false };
    private CancellationTokenSource? _cancel;
    private bool _busy;

    internal LauncherForm()
    {
        Text = "DMA Launcher";
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? Icon;
        ClientSize = new Size(570, 432);
        MinimumSize = MaximumSize = Size;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.White;
        Font = new Font("Malgun Gothic", 9);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;

        _header.Location = new Point(24, 17);
        _sub.Location = new Point(26, 58);
        _version.Location = new Point(26, 94);
        _status.Location = new Point(26, 119);
        _progress.Location = new Point(26, 165);
        _start.Location = new Point(26, 197);
        _stop.Location = new Point(198, 197);
        _logBox.Location = new Point(26, 244);
        Controls.AddRange(new Control[] { _header, _sub, _version, _status, _progress, _start, _stop, _logBox });
        _start.Click += async (_, _) => await StartAsync();
        _stop.Click += (_, _) => _cancel?.Cancel();
        FormClosing += (_, _) => _cancel?.Cancel();
        try { Directory.CreateDirectory(VersionsRoot); }
        catch (Exception ex) { _status.Text = "설치 폴더에 접근할 수 없습니다: " + ex.Message; }
        UpdateInstalledVersion();
        Log("런처 준비 완료 · GitHub 저장소: " + Repo);
    }

    private static HttpClient MakeHttpClient()
    {
        var c = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate });
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DMA-Launcher", "1.0"));
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        c.Timeout = TimeSpan.FromMinutes(3);
        return c;
    }

    private void UI(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;
        if (InvokeRequired) BeginInvoke(action);
        else action();
    }

    private void Status(string message) => UI(() => _status.Text = message);

    private void Log(string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
        try
        {
            Directory.CreateDirectory(DataRoot);
            File.AppendAllText(LogPath, line + Environment.NewLine, new UTF8Encoding(false));
        }
        catch { /* Logging failure must not prevent repair. */ }
        UI(() =>
        {
            _logBox.AppendText(line + Environment.NewLine);
            if (_logBox.TextLength > 40000) _logBox.Text = _logBox.Text[^20000..];
        });
    }

    private void UpdateInstalledVersion()
    {
        var local = FindLatestLocal();
        UI(() => _version.Text = "설치된 DMA: " + (local?.Version.ToString(3) ?? "없음"));
    }

    private async Task StartAsync()
    {
        if (_busy) return;
        _busy = true;
        _cancel = new CancellationTokenSource();
        _start.Enabled = false;
        _stop.Enabled = true;
        var token = _cancel.Token;
        int failedAttempts = 0;
        try
        {
            // User policy: no fixed retry ceiling. Retry until a DMA window appears or the user stops.
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await InstallAndStartAsync(token, forceReinstall: failedAttempts > 0);
                    Log("DMA 메인 창 확인. 런처 작업 완료.");
                    Status("DMA가 실행되었습니다.");
                    break;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    failedAttempts++;
                    Log("자동 복구 " + failedAttempts + "회차: " + ex.GetType().Name + " · " + ex.Message);
                    Status("실행 실패. 오류를 확인하고 계속 자동 복구합니다. (" + failedAttempts + "회)");
                    // Backoff is a delay, never a retry-count limit.
                    var delay = TimeSpan.FromSeconds(Math.Min(60, 5 * Math.Pow(2, Math.Min(failedAttempts - 1, 4))));
                    Log($"다음 복구 시도까지 {delay.TotalSeconds:0}초 · 중지하지 않으면 계속 반복");
                    await Task.Delay(delay, token);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            if (token.IsCancellationRequested) Status("사용자가 자동 복구를 중지했습니다.");
            _cancel.Dispose();
            _cancel = null;
            _busy = false;
            UI(() => { _start.Enabled = true; _stop.Enabled = false; _progress.Value = 0; });
        }
    }

    private async Task InstallAndStartAsync(CancellationToken token, bool forceReinstall)
    {
        Status("GitHub에서 최신 DMA 배포 버전을 확인합니다.");
        ReleasePackage? remote = null;
        try { remote = await GetLatestReleaseAsync(token); }
        catch (Exception ex)
        {
            Log("GitHub 연결 실패: " + ex.Message);
            if (FindLatestLocal() is null) throw;
            Log("설치된 DMA로 실행을 시도합니다.");
        }

        var local = FindLatestLocal();
        var target = remote is not null && (local is null || remote.Version >= local.Value.Version)
            ? remote.Version : local?.Version;
        if (target is null) throw new FileNotFoundException("GitHub Releases에 DMA_v버전.zip 배포 파일이 없으며 설치된 DMA도 없습니다.");

        var destination = VersionDirectory(target);
        if ((forceReinstall && remote is not null && remote.Version == target) || !File.Exists(Path.Combine(destination, "DMA.exe")))
        {
            if (remote is null || remote.Version != target)
                throw new FileNotFoundException("선택한 버전의 실행 파일이 없고 다운로드할 배포본도 없습니다.");
            await DownloadAndInstallAsync(remote, token);
        }
        UpdateInstalledVersion();
        await LaunchAndVerifyAsync(destination, token);
    }

    private static string VersionDirectory(Version version) => Path.Combine(VersionsRoot, version.ToString(3));

    private static (Version Version, string Directory)? FindLatestLocal()
    {
        if (!Directory.Exists(VersionsRoot)) return null;
        (Version Version, string Directory)? best = null;
        foreach (var dir in Directory.EnumerateDirectories(VersionsRoot))
        {
            if (!File.Exists(Path.Combine(dir, "DMA.exe"))) continue;
            if (!Version.TryParse(Path.GetFileName(dir), out var v)) continue;
            if (best is null || v > best.Value.Version) best = (v, dir);
        }
        return best;
    }

    private async Task<ReleasePackage?> GetLatestReleaseAsync(CancellationToken token)
    {
        using var response = await Http.GetAsync(ReleaseApi, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        ReleasePackage? best = null;
        foreach (var release in json.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) continue;
            string tag = release.GetProperty("tag_name").GetString() ?? "";
            string verStr = tag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? tag[1..] : tag;
            if (!Version.TryParse(verStr, out var version)) continue;
            string wantName = "DMA_v" + version.ToString(3) + ".zip";
            string? zip = null, hash = null;
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (name == wantName) zip = asset.GetProperty("browser_download_url").GetString();
                if (name == wantName + ".sha256") hash = asset.GetProperty("browser_download_url").GetString();
            }
            if (zip is not null && (best is null || version > best.Version))
                best = new ReleasePackage(version, tag, zip, hash);
        }
        if (best is not null) Log("GitHub 최신 DMA 버전: " + best.Version.ToString(3));
        else Log("GitHub Releases에 실행 파일 ZIP이 등록되어 있지 않습니다.");
        return best;
    }

    private async Task DownloadAndInstallAsync(ReleasePackage release, CancellationToken token)
    {
        string versionText = release.Version.ToString(3);
        string final = VersionDirectory(release.Version);
        string zipPath = Path.Combine(DataRoot, "download_" + versionText + "_" + Guid.NewGuid().ToString("N") + ".zip");
        string stage = Path.Combine(VersionsRoot, ".incoming_" + Guid.NewGuid().ToString("N"));
        string? movedOld = null;
        try
        {
            Status("DMA v" + versionText + " 다운로드 중...");
            await DownloadToFileAsync(release.ZipUrl, zipPath, token);
            if (release.HashUrl is not null)
            {
                string check = await Http.GetStringAsync(release.HashUrl, token);
                var match = Regex.Match(check, @"\b[A-Fa-f0-9]{64}\b");
                if (!match.Success) throw new InvalidDataException("SHA-256 검증 파일의 해시 형식이 잘못되었습니다.");
                using var file = File.OpenRead(zipPath);
                string actual = Convert.ToHexString(SHA256.HashData(file));
                if (!actual.Equals(match.Value, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("다운로드한 DMA ZIP의 SHA-256이 일치하지 않습니다.");
                Log("SHA-256 무결성 검증 완료");
            }
            else Log("SHA-256 배포 파일 미등록 · ZIP 압축 검사 및 실행 파일 존재 검사 진행");

            Status("DMA v" + versionText + " 설치 중...");
            Directory.CreateDirectory(stage);
            await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, stage, true), token);
            if (!File.Exists(Path.Combine(stage, "DMA.exe")))
                throw new InvalidDataException("배포 ZIP에 DMA.exe가 없습니다. 소스 ZIP은 설치할 수 없습니다.");
            if (Directory.Exists(final))
            {
                movedOld = final + ".old_" + Guid.NewGuid().ToString("N");
                Directory.Move(final, movedOld);
            }
            try { Directory.Move(stage, final); }
            catch
            {
                if (movedOld is not null && !Directory.Exists(final)) Directory.Move(movedOld, final);
                movedOld = null;
                throw;
            }
            if (movedOld is not null) { try { Directory.Delete(movedOld, true); } catch { } }
            Log("DMA v" + versionText + " 설치 완료 · " + final);
        }
        finally
        {
            try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { }
            try { if (Directory.Exists(stage)) Directory.Delete(stage, true); } catch { }
        }
    }

    private async Task DownloadToFileAsync(string url, string destination, CancellationToken token)
    {
        using var res = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        res.EnsureSuccessStatusCode();
        long? total = res.Content.Headers.ContentLength;
        await using var input = await res.Content.ReadAsStreamAsync(token);
        await using var file = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        var buffer = new byte[131072];
        long received = 0;
        while (true)
        {
            int read = await input.ReadAsync(buffer, token);
            if (read == 0) break;
            await file.WriteAsync(buffer.AsMemory(0, read), token);
            received += read;
            if (total is > 0)
            {
                int percent = (int)Math.Clamp(received * 100L / total.Value, 0, 100);
                UI(() => _progress.Value = percent);
            }
        }
        if (received == 0) throw new InvalidDataException("다운로드한 파일이 비어 있습니다.");
        Log("다운로드 완료: " + received.ToString("N0") + " bytes");
        UI(() => _progress.Value = 0);
    }

    private async Task LaunchAndVerifyAsync(string dir, CancellationToken token)
    {
        string executable = Path.Combine(dir, "DMA.exe");
        if (!File.Exists(executable)) throw new FileNotFoundException("DMA.exe 없음", executable);
        Status("DMA 실행 및 메인 창 확인 중...");
        Process? process = null;
        bool verified = false;
        try
        {
            process = Process.Start(new ProcessStartInfo(executable)
            {
                WorkingDirectory = dir,
                UseShellExecute = false
            }) ?? throw new InvalidOperationException("DMA.exe 실행 결과가 null입니다.");
            Log("DMA 프로세스 시작 · PID=" + process.Id);
            for (int i = 0; i < 150; i++)
            {
                token.ThrowIfCancellationRequested();
                process.Refresh();
                if (process.HasExited) throw new InvalidOperationException("DMA 메인 창 표시 전 종료 · 종료 코드 " + process.ExitCode);
                if (process.MainWindowHandle != IntPtr.Zero)
                {
                    verified = true;
                    Log("DMA 메인 창 감지 · v" + Path.GetFileName(dir));
                    return;
                }
                await Task.Delay(200, token);
            }
            throw new TimeoutException("DMA 실행 후 30초 동안 메인 창이 표시되지 않았습니다.");
        }
        finally
        {
            if (!verified && process is not null)
            {
                // No verified main window: ensure a stranded process cannot prevent repair.
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (Exception ex) { Log("비정상 프로세스 정리 실패: " + ex.Message); }
            }
            process?.Dispose();
        }
    }
}