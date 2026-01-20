/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Unity Technologies.
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

#include <iostream>
#include <sstream>
#include <string>
#include <functional>
#include <filesystem>
#include <windows.h>
#include <shlwapi.h>

#include <fcntl.h>
#include <io.h>

#include "BStrHolder.h"
#include "ComPtr.h"
#include "dte80a.tlh"

constexpr int RETRY_INTERVAL_MS = 150;
constexpr int TIMEOUT_MS = 10000;

// Often a DTE call made to Visual Studio can fail after Visual Studio has just started. Usually the
// return value will be RPC_E_CALL_REJECTED, meaning that Visual Studio is probably busy on another
// thread. This types filter the RPC messages and retries to send the message until VS accepts it.
class CRetryMessageFilter : public IMessageFilter
{
private:
	static bool ShouldRetryCall(DWORD dwTickCount, DWORD dwRejectType)
	{
		if (dwRejectType == SERVERCALL_RETRYLATER || dwRejectType == SERVERCALL_REJECTED) {
			return dwTickCount < TIMEOUT_MS;
		}

		return false;
	}

	win::ComPtr<IMessageFilter> currentFilter;

public:
	CRetryMessageFilter()
	{
		HRESULT hr = CoRegisterMessageFilter(this, &currentFilter);
		_ASSERT(SUCCEEDED(hr));
	}

	~CRetryMessageFilter()
	{
		win::ComPtr<IMessageFilter> messageFilter;
		HRESULT hr = CoRegisterMessageFilter(currentFilter, &messageFilter);
		_ASSERT(SUCCEEDED(hr));
	}

	// IUnknown methods
	IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv)
	{
		static const QITAB qit[] =
		{
			QITABENT(CRetryMessageFilter, IMessageFilter),
			{ 0 },
		};
		return QISearch(this, qit, riid, ppv);
	}

	IFACEMETHODIMP_(ULONG) AddRef()
	{
		return 0;
	}

	IFACEMETHODIMP_(ULONG) Release()
	{
		return 0;
	}

	DWORD STDMETHODCALLTYPE HandleInComingCall(DWORD dwCallType, HTASK htaskCaller, DWORD dwTickCount, LPINTERFACEINFO lpInterfaceInfo)
	{
		if (currentFilter)
			return currentFilter->HandleInComingCall(dwCallType, htaskCaller, dwTickCount, lpInterfaceInfo);

		return SERVERCALL_ISHANDLED;
	}

	DWORD STDMETHODCALLTYPE RetryRejectedCall(HTASK htaskCallee, DWORD dwTickCount, DWORD dwRejectType)
	{
		if (ShouldRetryCall(dwTickCount, dwRejectType))
			return RETRY_INTERVAL_MS;

		if (currentFilter)
			return currentFilter->RetryRejectedCall(htaskCallee, dwTickCount, dwRejectType);

		return (DWORD)-1;
	}

	DWORD STDMETHODCALLTYPE MessagePending(HTASK htaskCallee, DWORD dwTickCount, DWORD dwPendingType)
	{
		if (currentFilter)
			return currentFilter->MessagePending(htaskCallee, dwTickCount, dwPendingType);

		return PENDINGMSG_WAITDEFPROCESS;
	}
};

static void DisplayProgressbar() {
	std::wcout << "displayProgressBar" << std::endl;
}

static void ClearProgressbar() {
	std::wcout << "clearprogressbar" << std::endl;
}

inline std::wstring QuoteString(const std::wstring& str)
{
	return L"\"" + str + L"\"";
}

static std::wstring ErrorCodeToMsg(DWORD code)
{
	LPWSTR msgBuf = nullptr;
	if (!FormatMessageW(FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS,
						nullptr, code, MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT), (LPWSTR)&msgBuf, 0, nullptr))
	{
        return L"Unknown error";
	}
	else
	{
		std::wstring message(msgBuf);
		LocalFree(msgBuf);
		return message;
	}
}

// Get an environment variable
static std::wstring GetEnvironmentVariableValue(const std::wstring_view& variableName) {
	DWORD currentBufferSize = MAX_PATH;
	std::wstring variableValue;
	variableValue.resize(currentBufferSize);

	DWORD requiredBufferSize = GetEnvironmentVariableW(variableName.data(), variableValue.data(), currentBufferSize);
	if (requiredBufferSize == 0) {
		// Environment variable probably does not exist.
		return std::wstring();
	}

	if (currentBufferSize < requiredBufferSize) {
		variableValue.resize(requiredBufferSize);
		if (GetEnvironmentVariableW(variableName.data(), variableValue.data(), currentBufferSize) == 0)
			return std::wstring();
	}

	variableValue.resize(requiredBufferSize);
	return variableValue;
}

static bool StartVisualStudioProcess(
	const std::filesystem::path &visualStudioExecutablePath,
	const std::filesystem::path &solutionPath,
	DWORD *dwProcessId) {

	STARTUPINFOW si = { sizeof(si) };
	PROCESS_INFORMATION pi = {};

	const std::wstring startingDirectory = visualStudioExecutablePath.parent_path();

	// Build the command line that is passed as the argv of the VS process
	// argv[0] must be the quoted full path to the VS exe
	std::wstringstream commandLineStream;
	commandLineStream << QuoteString(visualStudioExecutablePath) << L" ";

	const std::wstring vsArgsWide = GetEnvironmentVariableValue(L"UNITY_VS_ARGS");
	if (!vsArgsWide.empty())
		commandLineStream << vsArgsWide << L" ";

	commandLineStream << QuoteString(solutionPath);

	std::wstring commandLine = commandLineStream.str();

	std::wcout << "Starting Visual Studio process with: " << commandLine << std::endl;

	const BOOL result = CreateProcessW(
		visualStudioExecutablePath.c_str(),					// Full path to VS, must not be quoted
		commandLine.data(),			// Command line, as passed as argv, separate arguments must be quoted if they contain spaces
		nullptr,					// Process handle not inheritable
		nullptr,					// Thread handle not inheritable
		false,						// Set handle inheritance to FALSE
		0,							// No creation flags
		nullptr,					// Use parent's environment block
		startingDirectory.c_str(),	// starting directory set to the VS directory
		&si,
		&pi);

	if (!result) {
		DWORD error = GetLastError();
		std::wcout << "Starting Visual Studio process failed: " << ErrorCodeToMsg(error) << std::endl;
		return false;
	}

	*dwProcessId = pi.dwProcessId;
	CloseHandle(pi.hProcess);
	CloseHandle(pi.hThread);

	return true;
}

static bool
MonikerIsVisualStudioProcess(const win::ComPtr<IMoniker> &moniker, const win::ComPtr<IBindCtx> &bindCtx, const DWORD dwProcessId = 0) {
	LPOLESTR oleMonikerName;
	if (FAILED(moniker->GetDisplayName(bindCtx, nullptr, &oleMonikerName)))
		return false;

	std::wstring monikerName(oleMonikerName);
	CoTaskMemFree(oleMonikerName);

	// VisualStudio Moniker is "!VisualStudio.DTE.$Version:$PID"
	// Example "!VisualStudio.DTE.14.0:1234"

	if (monikerName.find(L"!VisualStudio.DTE") != 0)
		return false;
	
	if (dwProcessId == 0)
		return true;

	const std::wstring suffix = L":" + std::to_wstring(dwProcessId);

	return monikerName.length() - suffix.length() == monikerName.find(suffix);
}

static win::ComPtr<EnvDTE::_DTE> FindVisualStudio(
	const DWORD pid,
	const std::function<bool(const win::ComPtr<EnvDTE::_DTE>&)> predicate)
{
	CRetryMessageFilter retryMessageFilter;

    win::ComPtr<IRunningObjectTable> ROT;
    if (FAILED(GetRunningObjectTable(0, &ROT)))
        return nullptr;

    win::ComPtr<IBindCtx> bindCtx;
    if (FAILED(CreateBindCtx(0, &bindCtx)))
        return nullptr;

    win::ComPtr<IEnumMoniker> enumMoniker;
    if (FAILED(ROT->EnumRunning(&enumMoniker)))
        return nullptr;

    win::ComPtr<IMoniker> moniker;
    ULONG monikersFetched = 0;
    while (SUCCEEDED(enumMoniker->Next(1, &moniker, &monikersFetched)) && monikersFetched) {
        if (!MonikerIsVisualStudioProcess(moniker, bindCtx, pid))
            continue;

        win::ComPtr<IUnknown> punk;
        if (FAILED(ROT->GetObject(moniker, &punk)))
            continue;

        win::ComPtr<EnvDTE::_DTE> dte;
        punk.As(&dte);
        if (!dte)
            continue;

        if (!predicate || predicate(dte))
            return dte;
    }
    return nullptr;
}

static win::ComPtr<EnvDTE::_DTE> FindRunningVisualStudioWithSolution(
	const std::filesystem::path &visualStudioExecutablePath,
	const std::filesystem::path &solutionPath)
{
	return FindVisualStudio(0, [&](const win::ComPtr<EnvDTE::_DTE>& dte) {
		CRetryMessageFilter retryMessageFilter;

		// Get the executable path of this running instance.
		BStrHolder visualStudioFullName;
		if (FAILED(dte->get_FullName(&visualStudioFullName)))
			return false;

		const std::filesystem::path currentVisualStudioExecutablePath = std::wstring(visualStudioFullName);

		// Ask for its current solution.
		win::ComPtr<EnvDTE::_Solution> solution;
		if (FAILED(dte->get_Solution(&solution)))
			return false;

		// Get the name of that solution.
		BStrHolder solutionFullName;
		if (FAILED(solution->get_FullName(&solutionFullName)))
			return false;

		const std::filesystem::path currentSolutionPath = std::wstring(solutionFullName);
		if (currentSolutionPath.empty())
			return false;

		std::wcout << "Visual Studio opened on " << currentSolutionPath.wstring() << std::endl;

		// If the name matches the solution we want to open and we have a Visual Studio installation path to use and this one matches that path, then use it.
		// If we don't have a Visual Studio installation path to use, just use this solution.
		if (!std::filesystem::equivalent(currentSolutionPath, solutionPath))
			return false;

		std::wcout << "We found a running Visual Studio session with the solution open." << std::endl;
		if (!visualStudioExecutablePath.empty()) {
			if (std::filesystem::equivalent(currentVisualStudioExecutablePath, visualStudioExecutablePath)) {
				return true;
			} else {
				std::wcout << "This running Visual Studio session does not seem to be the version requested in the user preferences. We will keep looking." << std::endl;
				return false;
			}
		} else {
			std::wcout << "We're not sure which version of Visual Studio was requested in the user preferences. We will use this running session." << std::endl;
			return true;
		}
	});
}

static win::ComPtr<EnvDTE::_DTE> FindRunningVisualStudioWithPID(const DWORD dwProcessId) {
	return FindVisualStudio(dwProcessId, nullptr);
}

// Waits for the solution to be opened, including SDK-style (CPS) projects.
// CPS projects load asynchronously after the solution is opened, and VS may restore
// the previous window state when they finish loading. This function polls until
// the solution reports as open
static bool WaitForSolutionOpened(const win::ComPtr<EnvDTE::_DTE> &dte) {
	CRetryMessageFilter retryMessageFilter;

	int timeWaited = 0;
	constexpr int SOLUTION_LOAD_TIMEOUT_MS = 60000;

	while (timeWaited < SOLUTION_LOAD_TIMEOUT_MS) {
		std::wcout << "Waiting for solution to be opened..." << std::endl;

		win::ComPtr<EnvDTE::_Solution> solution;
		if (FAILED(dte->get_Solution(&solution)) || !solution) {
			Sleep(RETRY_INTERVAL_MS);
			timeWaited += RETRY_INTERVAL_MS;
			continue;
		}

		// Check if solution is open
		VARIANT_BOOL isOpen = VARIANT_FALSE;
		if (FAILED(solution->get_IsOpen(&isOpen)) || isOpen == VARIANT_FALSE) {
			Sleep(RETRY_INTERVAL_MS);
			timeWaited += RETRY_INTERVAL_MS;
			continue;
		}

		return true;
	}

	std::wcout << "Timeout waiting for solution to be opened." << std::endl;
	return false;
}

static bool HaveRunningVisualStudioOpenFile(const win::ComPtr<EnvDTE::_DTE> &dte, const std::filesystem::path &filename, int line) {
	const BStrHolder bstrFileName(filename.c_str());
	const BStrHolder bstrKind(L"{00000000-0000-0000-0000-000000000000}"); // EnvDTE::vsViewKindPrimary
	win::ComPtr<EnvDTE::Window> window = nullptr;

	CRetryMessageFilter retryMessageFilter;

	if (!filename.empty()) {
		std::wcout << "Getting operations API from the Visual Studio session." << std::endl;

		win::ComPtr<EnvDTE::ItemOperations> item_ops;
		if (FAILED(dte->get_ItemOperations(&item_ops)))
			return false;

		std::wcout << "Waiting for the Visual Studio session to open the file: " << filename.wstring() << "." << std::endl;

		if (FAILED(item_ops->OpenFile(bstrFileName, bstrKind, &window)))
			return false;

		if (line > 0) {
			win::ComPtr<IDispatch> selection_dispatch;
			if (window && SUCCEEDED(window->get_Selection(&selection_dispatch))) {
				win::ComPtr<EnvDTE::TextSelection> selection;
				if (selection_dispatch &&
					SUCCEEDED(selection_dispatch->QueryInterface(__uuidof(EnvDTE::TextSelection), &selection)) &&
					selection) {
					selection->GotoLine(line, false);
					selection->EndOfLine(false);
				}
			}
		}
	}

	window = nullptr;
	if (SUCCEEDED(dte->get_MainWindow(&window))) {
		// Allow the DTE to make its main window the foreground
		HWND hWnd;
		window->get_HWnd((long*)&hWnd);

		DWORD processID;
		if (GetWindowThreadProcessId(hWnd, &processID) != 0)
			AllowSetForegroundWindow(processID);

		// Activate() set the window to visible and active (blinks in taskbar)
		window->Activate();
	}

	return true;
}

static bool VisualStudioOpenFile(
	const std::filesystem::path &visualStudioExecutablePath,
	const std::filesystem::path &solutionPath,
	const std::filesystem::path &filename,
	int line)
{
	win::ComPtr<EnvDTE::_DTE> dte = nullptr;

	std::wcout << "Looking for a running Visual Studio session." << std::endl;

	// TODO: If path does not exist pass empty, which will just try to match all windows with solution
	dte = FindRunningVisualStudioWithSolution(visualStudioExecutablePath, solutionPath);

	if (!dte) {
		std::wcout << "No appropriate running Visual Studio session not found, creating a new one." << std::endl;

		DisplayProgressbar();

		DWORD dwProcessId;
		if (!StartVisualStudioProcess(visualStudioExecutablePath, solutionPath, &dwProcessId)) {
			ClearProgressbar();
			return false;
		}

		int timeWaited = 0;

		while (timeWaited < TIMEOUT_MS) {
			dte = FindRunningVisualStudioWithPID(dwProcessId);

			if (dte)
				break;

			std::wcout << "Retrying to acquire DTE" << std::endl;

			Sleep(RETRY_INTERVAL_MS);
			timeWaited += RETRY_INTERVAL_MS;
		}

		ClearProgressbar();

		if (!dte)
			return false;

		WaitForSolutionOpened(dte);
	}
	else {
		std::wcout << "Using the existing Visual Studio session." << std::endl;
	}

	return HaveRunningVisualStudioOpenFile(dte, filename, line);
}

int wmain(int argc, wchar_t* argv[]) {

	// We need this to properly display UTF16 text on the console
	_setmode(_fileno(stdout), _O_U16TEXT);	
	
	if (argc != 3 && argc != 5) {
		std::wcerr << argc << ": wrong number of arguments\n" << "Usage: com.exe installationPath solutionPath [fileName lineNumber]" << std::endl;
		for (int i = 0; i < argc; i++) {
			std::wcerr << argv[i] << std::endl;
		}
		return EXIT_FAILURE;
	}

	if (FAILED(CoInitialize(nullptr))) {
		std::wcerr << "CoInitialize failed." << std::endl;
		return EXIT_FAILURE;
	}

	const std::filesystem::path visualStudioExecutablePath = std::filesystem::absolute(argv[1]);
	const std::filesystem::path solutionPath = std::filesystem::absolute(argv[2]);
	std::filesystem::path fileName = L"";
	int lineNumber = -1;

	if (argc == 5) {
		fileName = std::filesystem::absolute(argv[3]);
		lineNumber = std::stoi(argv[4]);
	}

	VisualStudioOpenFile(visualStudioExecutablePath, solutionPath, fileName, lineNumber);
	CoUninitialize();
	return EXIT_SUCCESS;
}
