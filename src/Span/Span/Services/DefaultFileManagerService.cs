using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Span.Services
{
    public class DefaultFileManagerService
    {
        private const string FolderOpenCommandKey = @"Software\Classes\Folder\shell\open\command";
        private const string DriveOpenCommandKey = @"Software\Classes\Drive\shell\open\command";

        // AppExecutionAlias 경로 (WindowsApps에 등록됨)
        private static readonly string AliasPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "WindowsApps", "spanfinder.exe");

        /// <summary>
        /// regedit /s + runas로 기본 파일 관리자 등록.
        /// UAC 다이얼로그가 표시됨.
        /// </summary>
        public async Task<bool> SetAsDefaultAsync()
        {
            try
            {
                var regContent = GenerateSetDefaultReg();
                var tempPath = Path.Combine(Path.GetTempPath(), "SpanSetDefault.reg");
                await File.WriteAllTextAsync(tempPath, regContent);

                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "regedit.exe",
                    Arguments = $"/s \"{tempPath}\"",
                    UseShellExecute = true,
                    Verb = "runas"  // UAC 승격
                });

                if (process != null)
                    await process.WaitForExitAsync();

                // temp 파일 정리
                try { File.Delete(tempPath); } catch { }

                // 검증
                return IsDefault();
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // 사용자가 UAC를 취소한 경우
                return false;
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[DefaultFM] SetAsDefault failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 기본 파일 관리자 해제.
        /// </summary>
        public async Task<bool> UnsetDefaultAsync()
        {
            try
            {
                var regContent = GenerateRestoreReg();
                var tempPath = Path.Combine(Path.GetTempPath(), "SpanRestoreDefault.reg");
                await File.WriteAllTextAsync(tempPath, regContent);

                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "regedit.exe",
                    Arguments = $"/s \"{tempPath}\"",
                    UseShellExecute = true,
                    Verb = "runas"
                });

                if (process != null)
                    await process.WaitForExitAsync();

                try { File.Delete(tempPath); } catch { }

                return !IsDefault();
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return false;
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[DefaultFM] UnsetDefault failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 현재 SPAN이 기본 파일 관리자인지 확인.
        /// HKCU에서 Folder\shell\open\command를 읽어 spanfinder.exe 포함 여부 확인.
        /// </summary>
        public bool IsDefault()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(FolderOpenCommandKey);
                var command = key?.GetValue("")?.ToString();
                return !string.IsNullOrEmpty(command)
                    && command.Contains("spanfinder.exe", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 등록용 .reg 파일을 사용자 지정 경로에 내보내기 (fallback용).
        /// </summary>
        public async Task ExportSetDefaultRegAsync(string filePath)
        {
            await File.WriteAllTextAsync(filePath, GenerateSetDefaultReg());
        }

        /// <summary>
        /// 복원용 .reg 파일을 사용자 지정 경로에 내보내기.
        /// </summary>
        public async Task ExportRestoreRegAsync(string filePath)
        {
            await File.WriteAllTextAsync(filePath, GenerateRestoreReg());
        }

        /// <summary>등록용 .reg 내용 생성</summary>
        private string GenerateSetDefaultReg()
        {
            // %LOCALAPPDATA%\Microsoft\WindowsApps\spanfinder.exe 전체 경로 사용 (안정성)
            var exePath = AliasPath.Replace("\\", "\\\\");
            return $"""
Windows Registry Editor Version 5.00

[HKEY_CURRENT_USER\Software\Classes\Folder\shell\open\command]
@="\"{exePath}\" \"%1\""
"DelegateExecute"=""

[HKEY_CURRENT_USER\Software\Classes\Drive\shell\open\command]
@="\"{exePath}\" \"%1\""
"DelegateExecute"=""
""";
        }

        /// <summary>복원용 .reg 내용 생성</summary>
        private string GenerateRestoreReg()
        {
            return """
Windows Registry Editor Version 5.00

[-HKEY_CURRENT_USER\Software\Classes\Folder\shell\open\command]
[-HKEY_CURRENT_USER\Software\Classes\Drive\shell\open\command]
""";
        }
    }
}
