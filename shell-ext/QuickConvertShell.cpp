#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shobjidl_core.h>
#include <shlwapi.h>
#include <objbase.h>

#include <algorithm>
#include <atomic>
#include <cwctype>
#include <iterator>
#include <mutex>
#include <new>
#include <string>
#include <vector>

namespace
{
    // Must match AppxManifest.xml Clsid & AppId
    // {7C4E9B21-6A38-4F1D-9C52-8A1E3D4B6F09}
    static const CLSID CLSID_QuickConvertCommand = {
        0x7c4e9b21, 0x6a38, 0x4f1d, { 0x9c, 0x52, 0x8a, 0x1e, 0x3d, 0x4b, 0x6f, 0x09 }
    };

    HMODULE g_hModule = nullptr;
    std::atomic<LONG> g_moduleRefCount{ 0 };

    struct Preset
    {
        std::wstring id;
        int index = 0;
        std::wstring title;
        std::wstring target;
        std::vector<std::wstring> acceptExts;
    };

    std::wstring ToLowerW(std::wstring s)
    {
        for (wchar_t& ch : s)
        {
            ch = static_cast<wchar_t>(std::towlower(ch));
        }
        return s;
    }

    std::wstring Utf8ToWide(const std::string& utf8)
    {
        if (utf8.empty()) return std::wstring();
        int len = MultiByteToWideChar(CP_UTF8, 0, utf8.data(), static_cast<int>(utf8.size()), nullptr, 0);
        if (len <= 0) return std::wstring();
        std::wstring out(static_cast<size_t>(len), L'\0');
        MultiByteToWideChar(CP_UTF8, 0, utf8.data(), static_cast<int>(utf8.size()), &out[0], len);
        return out;
    }

    std::wstring GetModuleDirectory()
    {
        wchar_t buf[MAX_PATH * 4] = {};
        DWORD len = GetModuleFileNameW(g_hModule, buf, static_cast<DWORD>(std::size(buf)));
        if (len == 0 || len >= std::size(buf)) return L"";
        std::wstring full(buf, len);
        size_t pos = full.find_last_of(L"\\/");
        return (pos == std::wstring::npos) ? L"" : full.substr(0, pos);
    }

    bool FileExistsW(const std::wstring& path)
    {
        DWORD attr = GetFileAttributesW(path.c_str());
        return (attr != INVALID_FILE_ATTRIBUTES) && ((attr & FILE_ATTRIBUTE_DIRECTORY) == 0);
    }

    std::wstring ResolveIconPath()
    {
        std::wstring dir = GetModuleDirectory();
        if (dir.empty()) return L"";
        std::wstring ico = dir + L"\\QuickConvert.ico";
        if (FileExistsW(ico)) return ico;
        std::wstring exe = dir + L"\\QuickConvert.exe";
        if (FileExistsW(exe)) return exe + L",0";
        return L"";
    }

    std::vector<std::wstring> SplitExts(const std::wstring& raw)
    {
        std::vector<std::wstring> exts;
        size_t start = 0;
        while (start <= raw.size())
        {
            size_t sep = raw.find(L'|', start);
            std::wstring token = (sep == std::wstring::npos) ? raw.substr(start) : raw.substr(start, sep - start);

            // Trim whitespace
            size_t b = 0;
            while (b < token.size() && std::iswspace(token[b])) ++b;
            size_t e = token.size();
            while (e > b && std::iswspace(token[e - 1])) --e;
            token = ToLowerW(token.substr(b, e - b));

            if (!token.empty())
            {
                if (token[0] != L'.') token.insert(token.begin(), L'.');
                exts.push_back(token);
            }
            if (sep == std::wstring::npos) break;
            start = sep + 1;
        }
        return exts;
    }

    // Minimal, self-contained UTF-8 JSON reader for presets.json array of objects
    class MiniJsonParser
    {
    public:
        explicit MiniJsonParser(const std::string& src) : _s(src), _pos(0)
        {
            // Skip UTF-8 BOM if present
            if (_s.size() >= 3 &&
                static_cast<unsigned char>(_s[0]) == 0xEF &&
                static_cast<unsigned char>(_s[1]) == 0xBB &&
                static_cast<unsigned char>(_s[2]) == 0xBF)
            {
                _pos = 3;
            }
        }

        std::vector<Preset> ParsePresets()
        {
            std::vector<Preset> list;
            SkipWs();
            if (!Consume('[')) return list;

            while (true)
            {
                SkipWs();
                if (Peek() == ']' || _pos >= _s.size()) break;

                Preset item;
                if (ParseObject(item))
                {
                    if (!item.title.empty() && !item.target.empty() && !item.acceptExts.empty())
                    {
                        list.push_back(std::move(item));
                    }
                }
                else
                {
                    break;
                }

                SkipWs();
                if (!Consume(',')) break;
            }

            std::sort(list.begin(), list.end(), [](const Preset& a, const Preset& b) {
                return a.index < b.index;
            });
            return list;
        }

    private:
        const std::string& _s;
        size_t _pos;

        char Peek() const
        {
            return (_pos < _s.size()) ? _s[_pos] : '\0';
        }

        void SkipWs()
        {
            while (_pos < _s.size())
            {
                unsigned char c = static_cast<unsigned char>(_s[_pos]);
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') ++_pos;
                else break;
            }
        }

        bool Consume(char expected)
        {
            if (_pos < _s.size() && _s[_pos] == expected)
            {
                ++_pos;
                return true;
            }
            return false;
        }

        bool ParseString(std::string& out)
        {
            out.clear();
            if (!Consume('"')) return false;
            while (_pos < _s.size())
            {
                char c = _s[_pos++];
                if (c == '"') return true;
                if (c == '\\')
                {
                    if (_pos >= _s.size()) return false;
                    char esc = _s[_pos++];
                    switch (esc)
                    {
                    case '"': out.push_back('"'); break;
                    case '\\': out.push_back('\\'); break;
                    case '/': out.push_back('/'); break;
                    case 'b': out.push_back('\b'); break;
                    case 'f': out.push_back('\f'); break;
                    case 'n': out.push_back('\n'); break;
                    case 'r': out.push_back('\r'); break;
                    case 't': out.push_back('\t'); break;
                    case 'u':
                    {
                        if (_pos + 4 > _s.size()) return false;
                        unsigned int cp = 0;
                        for (int i = 0; i < 4; ++i)
                        {
                            char h = _s[_pos++];
                            cp <<= 4;
                            if (h >= '0' && h <= '9') cp |= static_cast<unsigned int>(h - '0');
                            else if (h >= 'a' && h <= 'f') cp |= static_cast<unsigned int>(h - 'a' + 10);
                            else if (h >= 'A' && h <= 'F') cp |= static_cast<unsigned int>(h - 'A' + 10);
                            else return false;
                        }
                        AppendCodePointUtf8(out, cp);
                        break;
                    }
                    default:
                        out.push_back(esc);
                        break;
                    }
                }
                else
                {
                    out.push_back(c);
                }
            }
            return false;
        }

        static void AppendCodePointUtf8(std::string& out, unsigned int cp)
        {
            if (cp <= 0x7F)
            {
                out.push_back(static_cast<char>(cp));
            }
            else if (cp <= 0x7FF)
            {
                out.push_back(static_cast<char>(0xC0 | ((cp >> 6) & 0x1F)));
                out.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
            }
            else
            {
                out.push_back(static_cast<char>(0xE0 | ((cp >> 12) & 0x0F)));
                out.push_back(static_cast<char>(0x80 | ((cp >> 6) & 0x3F)));
                out.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
            }
        }

        bool ParseInt(int& out)
        {
            SkipWs();
            size_t start = _pos;
            if (Peek() == '-') ++_pos;
            while (_pos < _s.size() && _s[_pos] >= '0' && _s[_pos] <= '9') ++_pos;
            if (_pos == start) return false;
            try
            {
                out = std::stoi(_s.substr(start, _pos - start));
                return true;
            }
            catch (...)
            {
                return false;
            }
        }

        void SkipValue()
        {
            SkipWs();
            char c = Peek();
            if (c == '"')
            {
                std::string dummy;
                ParseString(dummy);
            }
            else if (c == '{')
            {
                int depth = 0;
                while (_pos < _s.size())
                {
                    char ch = _s[_pos++];
                    if (ch == '"')
                    {
                        --_pos;
                        std::string dummy;
                        if (!ParseString(dummy)) break;
                    }
                    else if (ch == '{') ++depth;
                    else if (ch == '}')
                    {
                        --depth;
                        if (depth <= 0) break;
                    }
                }
            }
            else if (c == '[')
            {
                int depth = 0;
                while (_pos < _s.size())
                {
                    char ch = _s[_pos++];
                    if (ch == '"')
                    {
                        --_pos;
                        std::string dummy;
                        if (!ParseString(dummy)) break;
                    }
                    else if (ch == '[') ++depth;
                    else if (ch == ']')
                    {
                        --depth;
                        if (depth <= 0) break;
                    }
                }
            }
            else
            {
                while (_pos < _s.size())
                {
                    char ch = _s[_pos];
                    if (ch == ',' || ch == '}' || ch == ']' || ch == ' ' || ch == '\r' || ch == '\n' || ch == '\t') break;
                    ++_pos;
                }
            }
        }

        bool ParseObject(Preset& item)
        {
            if (!Consume('{')) return false;
            while (true)
            {
                SkipWs();
                if (Consume('}')) return true;

                std::string key;
                if (!ParseString(key)) return false;

                SkipWs();
                if (!Consume(':')) return false;
                SkipWs();

                if (key == "id")
                {
                    std::string v;
                    if (!ParseString(v)) return false;
                    item.id = Utf8ToWide(v);
                }
                else if (key == "title")
                {
                    std::string v;
                    if (!ParseString(v)) return false;
                    item.title = Utf8ToWide(v);
                }
                else if (key == "target")
                {
                    std::string v;
                    if (!ParseString(v)) return false;
                    item.target = ToLowerW(Utf8ToWide(v));
                }
                else if (key == "acceptExts")
                {
                    std::string v;
                    if (!ParseString(v)) return false;
                    item.acceptExts = SplitExts(Utf8ToWide(v));
                }
                else if (key == "index")
                {
                    int v = 0;
                    if (!ParseInt(v)) return false;
                    item.index = v;
                }
                else
                {
                    SkipValue();
                }

                SkipWs();
                if (Consume(',')) continue;
                if (Consume('}')) return true;
                return false;
            }
        }
    };

    std::vector<Preset> GetFallbackPresets()
    {
        std::vector<Preset> p;
        p.push_back({ L"qc_01_mp3", 10, L"MP3 \u306B\u5909\u63DB", L"mp3", SplitExts(L".mp4|.mov|.mkv|.avi|.webm|.wav|.flac|.m4a") });
        p.push_back({ L"qc_02_mov", 20, L"MOV \u306B\u5909\u63DB", L"mov", SplitExts(L".mp4|.mkv|.avi|.webm") });
        p.push_back({ L"qc_03_mp4", 30, L"MP4 \u306B\u5909\u63DB", L"mp4", SplitExts(L".mov|.mkv|.avi|.webm|.gif") });
        p.push_back({ L"qc_09_png", 90, L"PNG \u306B\u5909\u63DB", L"png", SplitExts(L".jpg|.jpeg|.webp|.bmp|.gif") });
        p.push_back({ L"qc_10_jpg", 100, L"JPG \u306B\u5909\u63DB", L"jpg", SplitExts(L".png|.webp|.bmp|.gif") });
        return p;
    }

    std::wstring FindPresetsFilePath()
    {
        // 1. User override in %LOCALAPPDATA%\QuickConvert\presets.json
        wchar_t localAppData[MAX_PATH * 2] = {};
        DWORD n = GetEnvironmentVariableW(L"LOCALAPPDATA", localAppData, static_cast<DWORD>(std::size(localAppData)));
        if (n > 0 && n < std::size(localAppData))
        {
            std::wstring userOverride = std::wstring(localAppData) + L"\\QuickConvert\\presets.json";
            if (FileExistsW(userOverride)) return userOverride;
        }

        // 2. Next to DLL (installed MSIX or registered folder)
        std::wstring dir = GetModuleDirectory();
        if (!dir.empty())
        {
            std::wstring sibling = dir + L"\\presets.json";
            if (FileExistsW(sibling)) return sibling;

            size_t parentPos = dir.find_last_of(L"\\/");
            if (parentPos != std::wstring::npos)
            {
                std::wstring parent = dir.substr(0, parentPos) + L"\\presets.json";
                if (FileExistsW(parent)) return parent;
            }
        }
        return L"";
    }

    std::string ReadAllBytesUtf8(const std::wstring& path)
    {
        HANDLE h = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (h == INVALID_HANDLE_VALUE) return "";

        LARGE_INTEGER sz = {};
        std::string content;
        if (GetFileSizeEx(h, &sz) && sz.QuadPart > 0 && sz.QuadPart < 2 * 1024 * 1024)
        {
            content.resize(static_cast<size_t>(sz.QuadPart));
            DWORD read = 0;
            if (!ReadFile(h, &content[0], static_cast<DWORD>(sz.QuadPart), &read, nullptr))
            {
                content.clear();
            }
            else
            {
                content.resize(read);
            }
        }
        CloseHandle(h);
        return content;
    }

    std::mutex g_presetMutex;
    std::wstring g_cachedPresetsPath;
    FILETIME g_cachedWriteTime = {};
    std::vector<Preset> g_cachedPresets;

    std::vector<Preset> LoadPresetsCached()
    {
        std::lock_guard<std::mutex> lock(g_presetMutex);
        std::wstring path = FindPresetsFilePath();
        if (path.empty())
        {
            if (g_cachedPresets.empty())
            {
                g_cachedPresets = GetFallbackPresets();
            }
            return g_cachedPresets;
        }

        WIN32_FILE_ATTRIBUTE_DATA data = {};
        if (GetFileAttributesExW(path.c_str(), GetFileExInfoStandard, &data))
        {
            if (path == g_cachedPresetsPath &&
                data.ftLastWriteTime.dwLowDateTime == g_cachedWriteTime.dwLowDateTime &&
                data.ftLastWriteTime.dwHighDateTime == g_cachedWriteTime.dwHighDateTime &&
                !g_cachedPresets.empty())
            {
                return g_cachedPresets;
            }

            std::string raw = ReadAllBytesUtf8(path);
            MiniJsonParser parser(raw);
            std::vector<Preset> parsed = parser.ParsePresets();
            if (!parsed.empty())
            {
                g_cachedPresetsPath = path;
                g_cachedWriteTime = data.ftLastWriteTime;
                g_cachedPresets = std::move(parsed);
                return g_cachedPresets;
            }
        }

        if (g_cachedPresets.empty())
        {
            g_cachedPresets = GetFallbackPresets();
        }
        return g_cachedPresets;
    }

    bool ExtractExtensionLower(const std::wstring& fullPath, std::wstring& outExt)
    {
        size_t slash = fullPath.find_last_of(L"\\/");
        size_t dot = fullPath.find_last_of(L'.');
        if (dot == std::wstring::npos) return false;
        if (slash != std::wstring::npos && dot < slash) return false;
        if (dot + 1 >= fullPath.size()) return false;
        outExt = ToLowerW(fullPath.substr(dot));
        return true;
    }

    bool ExtractSelectedFiles(IShellItemArray* psiItemArray, std::vector<std::wstring>& outPaths, std::vector<std::wstring>& outExts)
    {
        outPaths.clear();
        outExts.clear();
        if (!psiItemArray) return false;

        DWORD count = 0;
        if (FAILED(psiItemArray->GetCount(&count)) || count == 0) return false;

        outPaths.reserve(count);
        outExts.reserve(count);

        for (DWORD i = 0; i < count; ++i)
        {
            IShellItem* item = nullptr;
            if (FAILED(psiItemArray->GetItemAt(i, &item)) || !item) return false;

            SFGAOF attrs = 0;
            HRESULT hrAttr = item->GetAttributes(SFGAO_FILESYSTEM | SFGAO_FOLDER, &attrs);
            if (FAILED(hrAttr) || (attrs & SFGAO_FILESYSTEM) == 0 || (attrs & SFGAO_FOLDER) != 0)
            {
                item->Release();
                return false;
            }

            PWSTR pszPath = nullptr;
            HRESULT hrPath = item->GetDisplayName(SIGDN_FILESYSPATH, &pszPath);
            item->Release();
            if (FAILED(hrPath) || !pszPath) return false;

            std::wstring path(pszPath);
            CoTaskMemFree(pszPath);

            std::wstring ext;
            if (!ExtractExtensionLower(path, ext)) return false;

            outPaths.push_back(std::move(path));
            outExts.push_back(std::move(ext));
        }

        return !outPaths.empty();
    }

    bool PresetMatchesAllExts(const Preset& preset, const std::vector<std::wstring>& exts)
    {
        if (exts.empty()) return false;
        for (const std::wstring& ext : exts)
        {
            bool found = false;
            for (const std::wstring& allowed : preset.acceptExts)
            {
                if (ext == allowed)
                {
                    found = true;
                    break;
                }
            }
            if (!found) return false;
        }
        return true;
    }

    class SubConvertCommand final : public IExplorerCommand
    {
    public:
        explicit SubConvertCommand(const Preset& preset)
            : _refCount(1), _preset(preset)
        {
            ++g_moduleRefCount;
        }

        ~SubConvertCommand()
        {
            --g_moduleRefCount;
        }

        // IUnknown
        IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv) override
        {
            if (!ppv) return E_POINTER;
            *ppv = nullptr;
            if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_IExplorerCommand))
            {
                *ppv = static_cast<IExplorerCommand*>(this);
                AddRef();
                return S_OK;
            }
            return E_NOINTERFACE;
        }

        IFACEMETHODIMP_(ULONG) AddRef() override
        {
            return static_cast<ULONG>(++_refCount);
        }

        IFACEMETHODIMP_(ULONG) Release() override
        {
            LONG c = --_refCount;
            if (c == 0) delete this;
            return static_cast<ULONG>(c);
        }

        // IExplorerCommand
        IFACEMETHODIMP GetTitle(IShellItemArray*, LPWSTR* ppszName) override
        {
            if (!ppszName) return E_POINTER;
            return SHStrDupW(_preset.title.c_str(), ppszName);
        }

        IFACEMETHODIMP GetIcon(IShellItemArray*, LPWSTR* ppszIcon) override
        {
            if (!ppszIcon) return E_POINTER;
            std::wstring icon = ResolveIconPath();
            if (icon.empty())
            {
                *ppszIcon = nullptr;
                return E_NOTIMPL;
            }
            return SHStrDupW(icon.c_str(), ppszIcon);
        }

        IFACEMETHODIMP GetToolTip(IShellItemArray*, LPWSTR* ppszInfotip) override
        {
            if (ppszInfotip) *ppszInfotip = nullptr;
            return E_NOTIMPL;
        }

        IFACEMETHODIMP GetCanonicalName(GUID* pguidCommandName) override
        {
            if (pguidCommandName) *pguidCommandName = GUID_NULL;
            return E_NOTIMPL;
        }

        IFACEMETHODIMP GetState(IShellItemArray* psiItemArray, BOOL, EXPCMDSTATE* pCmdState) override
        {
            if (!pCmdState) return E_POINTER;
            std::vector<std::wstring> paths;
            std::vector<std::wstring> exts;
            if (!ExtractSelectedFiles(psiItemArray, paths, exts))
            {
                *pCmdState = ECS_HIDDEN;
                return S_OK;
            }
            *pCmdState = PresetMatchesAllExts(_preset, exts) ? ECS_ENABLED : ECS_HIDDEN;
            return S_OK;
        }

        IFACEMETHODIMP Invoke(IShellItemArray* psiItemArray, IBindCtx*) override
        {
            std::vector<std::wstring> paths;
            std::vector<std::wstring> exts;
            if (!ExtractSelectedFiles(psiItemArray, paths, exts))
            {
                return S_OK;
            }

            std::wstring dir = GetModuleDirectory();
            std::wstring exePath = dir + L"\\QuickConvert.exe";
            std::wstring icoPath = dir + L"\\QuickConvert.ico";

            if (!FileExistsW(exePath))
            {
                return HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND);
            }

            std::wstring cmdLine = L"\"" + exePath + L"\" --to " + _preset.target;
            if (FileExistsW(icoPath))
            {
                cmdLine += L" --icon \"" + icoPath + L"\"";
            }
            for (const std::wstring& p : paths)
            {
                cmdLine += L" \"" + p + L"\"";
            }

            STARTUPINFOW si = {};
            si.cb = sizeof(si);
            PROCESS_INFORMATION pi = {};
            std::vector<wchar_t> mutableCmd(cmdLine.begin(), cmdLine.end());
            mutableCmd.push_back(L'\0');

            BOOL ok = CreateProcessW(
                exePath.c_str(),
                mutableCmd.data(),
                nullptr,
                nullptr,
                FALSE,
                0,
                nullptr,
                dir.empty() ? nullptr : dir.c_str(),
                &si,
                &pi);

            if (!ok)
            {
                return HRESULT_FROM_WIN32(GetLastError());
            }

            CloseHandle(pi.hThread);
            CloseHandle(pi.hProcess);
            return S_OK;
        }

        IFACEMETHODIMP GetFlags(EXPCMDFLAGS* pFlags) override
        {
            if (!pFlags) return E_POINTER;
            *pFlags = ECF_DEFAULT;
            return S_OK;
        }

        IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** ppEnum) override
        {
            if (ppEnum) *ppEnum = nullptr;
            return E_NOTIMPL;
        }

    private:
        std::atomic<LONG> _refCount;
        Preset _preset;
    };

    class SubCommandEnumerator final : public IEnumExplorerCommand
    {
    public:
        explicit SubCommandEnumerator(std::vector<Preset> presets)
            : _refCount(1), _presets(std::move(presets)), _index(0)
        {
            ++g_moduleRefCount;
        }

        ~SubCommandEnumerator()
        {
            --g_moduleRefCount;
        }

        IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv) override
        {
            if (!ppv) return E_POINTER;
            *ppv = nullptr;
            if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_IEnumExplorerCommand))
            {
                *ppv = static_cast<IEnumExplorerCommand*>(this);
                AddRef();
                return S_OK;
            }
            return E_NOINTERFACE;
        }

        IFACEMETHODIMP_(ULONG) AddRef() override
        {
            return static_cast<ULONG>(++_refCount);
        }

        IFACEMETHODIMP_(ULONG) Release() override
        {
            LONG c = --_refCount;
            if (c == 0) delete this;
            return static_cast<ULONG>(c);
        }

        IFACEMETHODIMP Next(ULONG celt, IExplorerCommand** pUICommand, ULONG* pceltFetched) override
        {
            if (!pUICommand) return E_POINTER;
            if (celt > 1 && !pceltFetched) return E_INVALIDARG;

            ULONG fetched = 0;
            while (fetched < celt && _index < _presets.size())
            {
                pUICommand[fetched] = new (std::nothrow) SubConvertCommand(_presets[_index]);
                if (!pUICommand[fetched])
                {
                    for (ULONG j = 0; j < fetched; ++j) pUICommand[j]->Release();
                    if (pceltFetched) *pceltFetched = 0;
                    return E_OUTOFMEMORY;
                }
                ++fetched;
                ++_index;
            }

            if (pceltFetched) *pceltFetched = fetched;
            return (fetched == celt) ? S_OK : S_FALSE;
        }

        IFACEMETHODIMP Skip(ULONG celt) override
        {
            if (_index + celt > _presets.size())
            {
                _index = _presets.size();
                return S_FALSE;
            }
            _index += celt;
            return S_OK;
        }

        IFACEMETHODIMP Reset() override
        {
            _index = 0;
            return S_OK;
        }

        IFACEMETHODIMP Clone(IEnumExplorerCommand** ppenum) override
        {
            if (!ppenum) return E_POINTER;
            auto* copy = new (std::nothrow) SubCommandEnumerator(_presets);
            if (!copy) return E_OUTOFMEMORY;
            copy->_index = _index;
            *ppenum = copy;
            return S_OK;
        }

    private:
        std::atomic<LONG> _refCount;
        std::vector<Preset> _presets;
        size_t _index;
    };

    class RootConvertCommand final : public IExplorerCommand
    {
    public:
        RootConvertCommand() : _refCount(1)
        {
            ++g_moduleRefCount;
        }

        ~RootConvertCommand()
        {
            --g_moduleRefCount;
        }

        IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv) override
        {
            if (!ppv) return E_POINTER;
            *ppv = nullptr;
            if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_IExplorerCommand))
            {
                *ppv = static_cast<IExplorerCommand*>(this);
                AddRef();
                return S_OK;
            }
            return E_NOINTERFACE;
        }

        IFACEMETHODIMP_(ULONG) AddRef() override
        {
            return static_cast<ULONG>(++_refCount);
        }

        IFACEMETHODIMP_(ULONG) Release() override
        {
            LONG c = --_refCount;
            if (c == 0) delete this;
            return static_cast<ULONG>(c);
        }

        IFACEMETHODIMP GetTitle(IShellItemArray*, LPWSTR* ppszName) override
        {
            if (!ppszName) return E_POINTER;
            // "ファイル変換"
            return SHStrDupW(L"\u30D5\u30A1\u30A4\u30EB\u5909\u63DB", ppszName);
        }

        IFACEMETHODIMP GetIcon(IShellItemArray*, LPWSTR* ppszIcon) override
        {
            if (!ppszIcon) return E_POINTER;
            std::wstring icon = ResolveIconPath();
            if (icon.empty())
            {
                *ppszIcon = nullptr;
                return E_NOTIMPL;
            }
            return SHStrDupW(icon.c_str(), ppszIcon);
        }

        IFACEMETHODIMP GetToolTip(IShellItemArray*, LPWSTR* ppszInfotip) override
        {
            if (ppszInfotip) *ppszInfotip = nullptr;
            return E_NOTIMPL;
        }

        IFACEMETHODIMP GetCanonicalName(GUID* pguidCommandName) override
        {
            if (!pguidCommandName) return E_POINTER;
            *pguidCommandName = CLSID_QuickConvertCommand;
            return S_OK;
        }

        IFACEMETHODIMP GetState(IShellItemArray* psiItemArray, BOOL, EXPCMDSTATE* pCmdState) override
        {
            if (!pCmdState) return E_POINTER;
            std::vector<std::wstring> paths;
            std::vector<std::wstring> exts;
            if (!ExtractSelectedFiles(psiItemArray, paths, exts))
            {
                *pCmdState = ECS_HIDDEN;
                return S_OK;
            }

            std::vector<Preset> presets = LoadPresetsCached();
            for (const Preset& p : presets)
            {
                if (PresetMatchesAllExts(p, exts))
                {
                    *pCmdState = ECS_ENABLED;
                    return S_OK;
                }
            }

            *pCmdState = ECS_HIDDEN;
            return S_OK;
        }

        IFACEMETHODIMP Invoke(IShellItemArray*, IBindCtx*) override
        {
            return S_OK;
        }

        IFACEMETHODIMP GetFlags(EXPCMDFLAGS* pFlags) override
        {
            if (!pFlags) return E_POINTER;
            *pFlags = ECF_HASSUBCOMMANDS;
            return S_OK;
        }

        IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** ppEnum) override
        {
            if (!ppEnum) return E_POINTER;
            std::vector<Preset> presets = LoadPresetsCached();
            auto* enumerator = new (std::nothrow) SubCommandEnumerator(std::move(presets));
            if (!enumerator)
            {
                *ppEnum = nullptr;
                return E_OUTOFMEMORY;
            }
            *ppEnum = enumerator;
            return S_OK;
        }

    private:
        std::atomic<LONG> _refCount;
    };

    class ClassFactory final : public IClassFactory
    {
    public:
        ClassFactory() : _refCount(1)
        {
            ++g_moduleRefCount;
        }

        ~ClassFactory()
        {
            --g_moduleRefCount;
        }

        IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv) override
        {
            if (!ppv) return E_POINTER;
            *ppv = nullptr;
            if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_IClassFactory))
            {
                *ppv = static_cast<IClassFactory*>(this);
                AddRef();
                return S_OK;
            }
            return E_NOINTERFACE;
        }

        IFACEMETHODIMP_(ULONG) AddRef() override
        {
            return static_cast<ULONG>(++_refCount);
        }

        IFACEMETHODIMP_(ULONG) Release() override
        {
            LONG c = --_refCount;
            if (c == 0) delete this;
            return static_cast<ULONG>(c);
        }

        IFACEMETHODIMP CreateInstance(IUnknown* pUnkOuter, REFIID riid, void** ppv) override
        {
            if (!ppv) return E_POINTER;
            *ppv = nullptr;
            if (pUnkOuter != nullptr) return CLASS_E_NOAGGREGATION;

            auto* cmd = new (std::nothrow) RootConvertCommand();
            if (!cmd) return E_OUTOFMEMORY;

            HRESULT hr = cmd->QueryInterface(riid, ppv);
            cmd->Release();
            return hr;
        }

        IFACEMETHODIMP LockServer(BOOL fLock) override
        {
            if (fLock) ++g_moduleRefCount;
            else --g_moduleRefCount;
            return S_OK;
        }

    private:
        std::atomic<LONG> _refCount;
    };
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID)
{
    if (ul_reason_for_call == DLL_PROCESS_ATTACH)
    {
        g_hModule = hModule;
        DisableThreadLibraryCalls(hModule);
    }
    return TRUE;
}

STDAPI DllCanUnloadNow()
{
    return (g_moduleRefCount.load() == 0) ? S_OK : S_FALSE;
}

STDAPI DllGetClassObject(REFCLSID rclsid, REFIID riid, void** ppv)
{
    if (!ppv) return E_POINTER;
    *ppv = nullptr;
    if (!IsEqualCLSID(rclsid, CLSID_QuickConvertCommand))
    {
        return CLASS_E_CLASSNOTAVAILABLE;
    }

    auto* factory = new (std::nothrow) ClassFactory();
    if (!factory) return E_OUTOFMEMORY;

    HRESULT hr = factory->QueryInterface(riid, ppv);
    factory->Release();
    return hr;
}
