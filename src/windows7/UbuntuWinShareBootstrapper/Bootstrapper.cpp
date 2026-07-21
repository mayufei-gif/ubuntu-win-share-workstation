#include <windows.h>
#include <shellapi.h>
#include <winsvc.h>
#include <shlobj.h>
#include <strsafe.h>
#include <string>
#include "resource.h"

namespace {

const wchar_t kProductName[] = L"Ubuntu Win Share";
const wchar_t kRepairArgument[] = L"--repair-dependencies";
const wchar_t kSelfTestArgument[] = L"--self-test";

struct OsVersionInfoEx {
  ULONG size;
  ULONG major;
  ULONG minor;
  ULONG build;
  ULONG platform;
  WCHAR service_pack[128];
  USHORT service_pack_major;
  USHORT service_pack_minor;
  USHORT suite_mask;
  UCHAR product_type;
  UCHAR reserved;
};

typedef LONG (WINAPI *RtlGetVersionFunction)(OsVersionInfoEx*);

std::wstring Quote(const std::wstring& value) {
  return L"\"" + value + L"\"";
}

bool HasArgument(const std::wstring& command_line, const wchar_t* argument) {
  return command_line.find(argument) != std::wstring::npos;
}

std::wstring ArgumentValue(const std::wstring& command_line, const wchar_t* name) {
  std::wstring needle = std::wstring(name) + L" ";
  std::wstring::size_type start = command_line.find(needle);
  if (start == std::wstring::npos) {
    return L"";
  }
  start += needle.size();
  if (start >= command_line.size()) {
    return L"";
  }
  if (command_line[start] == L'"') {
    ++start;
    std::wstring::size_type end = command_line.find(L'"', start);
    return command_line.substr(start, end == std::wstring::npos ? end : end - start);
  }
  std::wstring::size_type end = command_line.find(L' ', start);
  return command_line.substr(start, end == std::wstring::npos ? end : end - start);
}

bool IsWindows7Sp1() {
  HMODULE ntdll = GetModuleHandleW(L"ntdll.dll");
  if (ntdll == NULL) {
    return false;
  }
  RtlGetVersionFunction get_version =
      reinterpret_cast<RtlGetVersionFunction>(GetProcAddress(ntdll, "RtlGetVersion"));
  if (get_version == NULL) {
    return false;
  }
  OsVersionInfoEx info = {};
  info.size = sizeof(info);
  return get_version(&info) == 0 &&
      info.major == 6 &&
      info.minor == 1 &&
      info.build >= 7601 &&
      info.product_type == VER_NT_WORKSTATION;
}

bool IsNetFx35Installed() {
  HKEY key = NULL;
  LONG status = RegOpenKeyExW(
      HKEY_LOCAL_MACHINE,
      L"SOFTWARE\\Microsoft\\NET Framework Setup\\NDP\\v3.5",
      0,
      KEY_QUERY_VALUE | KEY_WOW64_32KEY,
      &key);
  if (status != ERROR_SUCCESS) {
    return false;
  }
  DWORD installed = 0;
  DWORD size = sizeof(installed);
  DWORD type = 0;
  status = RegQueryValueExW(
      key, L"Install", NULL, &type, reinterpret_cast<BYTE*>(&installed), &size);
  RegCloseKey(key);
  return status == ERROR_SUCCESS && type == REG_DWORD && installed == 1;
}

bool IsRobocopyAvailable() {
  WCHAR system_directory[MAX_PATH] = {};
  UINT length = GetSystemDirectoryW(system_directory, MAX_PATH);
  if (length == 0 || length >= MAX_PATH) {
    return false;
  }
  std::wstring path = std::wstring(system_directory) + L"\\robocopy.exe";
  return GetFileAttributesW(path.c_str()) != INVALID_FILE_ATTRIBUTES;
}

bool IsWorkstationRunning() {
  SC_HANDLE manager = OpenSCManagerW(NULL, NULL, SC_MANAGER_CONNECT);
  if (manager == NULL) {
    return false;
  }
  SC_HANDLE service = OpenServiceW(manager, L"LanmanWorkstation", SERVICE_QUERY_STATUS);
  if (service == NULL) {
    CloseServiceHandle(manager);
    return false;
  }
  SERVICE_STATUS_PROCESS status = {};
  DWORD returned = 0;
  bool result = QueryServiceStatusEx(
      service,
      SC_STATUS_PROCESS_INFO,
      reinterpret_cast<BYTE*>(&status),
      sizeof(status),
      &returned) != FALSE &&
      status.dwCurrentState == SERVICE_RUNNING;
  CloseServiceHandle(service);
  CloseServiceHandle(manager);
  return result;
}

bool StartWorkstationService() {
  SC_HANDLE manager = OpenSCManagerW(NULL, NULL, SC_MANAGER_CONNECT);
  if (manager == NULL) {
    return false;
  }
  SC_HANDLE service = OpenServiceW(
      manager, L"LanmanWorkstation", SERVICE_START | SERVICE_QUERY_STATUS);
  if (service == NULL) {
    CloseServiceHandle(manager);
    return false;
  }
  if (!StartServiceW(service, 0, NULL) && GetLastError() != ERROR_SERVICE_ALREADY_RUNNING) {
    CloseServiceHandle(service);
    CloseServiceHandle(manager);
    return false;
  }
  CloseServiceHandle(service);
  CloseServiceHandle(manager);
  return true;
}

bool RunHiddenAndWait(const std::wstring& application, const std::wstring& arguments) {
  STARTUPINFOW startup = {};
  startup.cb = sizeof(startup);
  startup.dwFlags = STARTF_USESHOWWINDOW;
  startup.wShowWindow = SW_HIDE;
  PROCESS_INFORMATION process = {};
  std::wstring command = Quote(application) + L" " + arguments;
  if (!CreateProcessW(
          NULL,
          &command[0],
          NULL,
          NULL,
          FALSE,
          CREATE_NO_WINDOW,
          NULL,
          NULL,
          &startup,
          &process)) {
    return false;
  }
  WaitForSingleObject(process.hProcess, INFINITE);
  DWORD code = 1;
  GetExitCodeProcess(process.hProcess, &code);
  CloseHandle(process.hThread);
  CloseHandle(process.hProcess);
  return code == 0;
}

bool RepairDependencies() {
  WCHAR system_directory[MAX_PATH] = {};
  UINT length = GetSystemDirectoryW(system_directory, MAX_PATH);
  if (length == 0 || length >= MAX_PATH) {
    return false;
  }
  std::wstring dism = std::wstring(system_directory) + L"\\dism.exe";
  if (!IsNetFx35Installed() &&
      !RunHiddenAndWait(
          dism,
          L"/online /enable-feature /featurename:NetFx3 /all /norestart")) {
    return false;
  }
  if (!IsWorkstationRunning() && !StartWorkstationService()) {
    return false;
  }
  return IsNetFx35Installed() && IsWorkstationRunning() && IsRobocopyAvailable();
}

bool RequireDependencies(const std::wstring& executable_path) {
  if (IsNetFx35Installed() && IsWorkstationRunning() && IsRobocopyAvailable()) {
    return true;
  }
  SHELLEXECUTEINFOW execute = {};
  execute.cbSize = sizeof(execute);
  execute.fMask = SEE_MASK_NOCLOSEPROCESS;
  execute.lpVerb = L"runas";
  execute.lpFile = executable_path.c_str();
  execute.lpParameters = kRepairArgument;
  execute.nShow = SW_HIDE;
  if (!ShellExecuteExW(&execute)) {
    return false;
  }
  WaitForSingleObject(execute.hProcess, INFINITE);
  DWORD code = 1;
  GetExitCodeProcess(execute.hProcess, &code);
  CloseHandle(execute.hProcess);
  return code == 0 &&
      IsNetFx35Installed() &&
      IsWorkstationRunning() &&
      IsRobocopyAvailable();
}

bool GetExecutablePath(std::wstring* path) {
  WCHAR buffer[MAX_PATH] = {};
  DWORD length = GetModuleFileNameW(NULL, buffer, MAX_PATH);
  if (length == 0 || length >= MAX_PATH) {
    return false;
  }
  *path = buffer;
  return true;
}

bool WriteFileBytes(const std::wstring& path, const BYTE* data, DWORD length) {
  HANDLE file = CreateFileW(
      path.c_str(),
      GENERIC_WRITE,
      0,
      NULL,
      CREATE_ALWAYS,
      FILE_ATTRIBUTE_NORMAL,
      NULL);
  if (file == INVALID_HANDLE_VALUE) {
    return false;
  }
  DWORD written = 0;
  bool result = WriteFile(file, data, length, &written, NULL) != FALSE && written == length;
  CloseHandle(file);
  return result;
}

bool ExtractClient(std::wstring* client_path) {
  HRSRC resource = FindResourceW(NULL, MAKEINTRESOURCEW(IDR_CLIENT_PAYLOAD), RT_RCDATA);
  if (resource == NULL) {
    return false;
  }
  HGLOBAL loaded = LoadResource(NULL, resource);
  DWORD size = SizeofResource(NULL, resource);
  const BYTE* bytes = static_cast<const BYTE*>(LockResource(loaded));
  if (loaded == NULL || bytes == NULL || size == 0) {
    return false;
  }
  WCHAR local_app_data[MAX_PATH] = {};
  if (SHGetFolderPathW(
          NULL,
          CSIDL_LOCAL_APPDATA | CSIDL_FLAG_CREATE,
          NULL,
          SHGFP_TYPE_CURRENT,
          local_app_data) != S_OK) {
    return false;
  }
  std::wstring directory = std::wstring(local_app_data) +
      L"\\UbuntuWinShare\\BootstrapperCache";
  if (!CreateDirectoryW((std::wstring(local_app_data) + L"\\UbuntuWinShare").c_str(), NULL) &&
      GetLastError() != ERROR_ALREADY_EXISTS) {
    return false;
  }
  if (!CreateDirectoryW(directory.c_str(), NULL) &&
      GetLastError() != ERROR_ALREADY_EXISTS) {
    return false;
  }
  *client_path = directory + L"\\UbuntuWinShareClientPayload.exe";
  return WriteFileBytes(*client_path, bytes, size);
}

bool LaunchClient(const std::wstring& client_path) {
  STARTUPINFOW startup = {};
  startup.cb = sizeof(startup);
  PROCESS_INFORMATION process = {};
  std::wstring command = Quote(client_path);
  bool started = CreateProcessW(
      NULL,
      &command[0],
      NULL,
      NULL,
      FALSE,
      0,
      NULL,
      NULL,
      &startup,
      &process) != FALSE;
  if (started) {
    CloseHandle(process.hThread);
    CloseHandle(process.hProcess);
  }
  return started;
}

bool WriteAsciiTextFile(const std::wstring& path, const std::wstring& text) {
  std::string ascii;
  ascii.reserve(text.size());
  for (std::wstring::size_type index = 0; index < text.size(); ++index) {
    if (text[index] > 0x7f) {
      return false;
    }
    ascii.push_back(static_cast<char>(text[index]));
  }
  return WriteFileBytes(
      path,
      reinterpret_cast<const BYTE*>(ascii.data()),
      static_cast<DWORD>(ascii.size()));
}

int RunSelfTest(const std::wstring& command_line) {
  std::wstring result_path = ArgumentValue(command_line, L"--result");
  if (result_path.empty()) {
    return 2;
  }
  HRSRC resource = FindResourceW(NULL, MAKEINTRESOURCEW(IDR_CLIENT_PAYLOAD), RT_RCDATA);
  std::wstring result = L"WIN7_BOOTSTRAPPER_SELF_TEST_OK\r\n";
  result += resource != NULL ? L"CLIENT_PAYLOAD=present\r\n" : L"CLIENT_PAYLOAD=missing\r\n";
  result += IsRobocopyAvailable() ? L"ROBOCOPY=present\r\n" : L"ROBOCOPY=missing\r\n";
  return resource != NULL && WriteAsciiTextFile(result_path, result) ? 0 : 1;
}

void ShowError(const wchar_t* text) {
  MessageBoxW(NULL, text, kProductName, MB_OK | MB_ICONERROR);
}

}  // namespace

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR command_line, int) {
  std::wstring arguments = command_line == NULL ? L"" : command_line;
  if (HasArgument(arguments, kSelfTestArgument)) {
    return RunSelfTest(arguments);
  }
  if (HasArgument(arguments, kRepairArgument)) {
    return RepairDependencies() ? 0 : 1;
  }
  if (!IsWindows7Sp1()) {
    ShowError(L"此安装包仅支持 Windows 7 SP1。未执行安装或启动。");
    return 3;
  }
  std::wstring executable_path;
  if (!GetExecutablePath(&executable_path) || !RequireDependencies(executable_path)) {
    ShowError(L"Windows 组件修复未完成。请使用包内的人工兜底安装脚本后重试。");
    return 4;
  }
  std::wstring client_path;
  if (!ExtractClient(&client_path) || !LaunchClient(client_path)) {
    ShowError(L"无法启动 Ubuntu Win Share 客户端。请重新下载安装包。");
    return 5;
  }
  return 0;
}
