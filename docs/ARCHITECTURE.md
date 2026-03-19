# SPAN Finder — Architecture

> 시스템 아키텍처, DI 의존성 그래프, 데이터 플로우. 코드 수정 시 영향 범위를 파악하기 위한 참조 문서.
> 최종 갱신: 2026-03-15

---

## System Overview

```
┌─────────────────────────────────────────────────────────────┐
│  MainWindow (8 partial files)                               │
│  ├─ KeyboardHandler    ├─ NavigationManager                 │
│  ├─ TabManager         ├─ SplitPreviewManager               │
│  ├─ DragDropHandler    ├─ FileOperationHandler              │
│  ├─ SettingsHandler    └─ Core (WndProc, Init, Cleanup)     │
├─────────────────────────────────────────────────────────────┤
│  Views                                                      │
│  ├─ DetailsModeView  ├─ ListModeView  ├─ IconModeView       │
│  ├─ HomeModeView     ├─ SettingsModeView                    │
│  ├─ PreviewPanelView ├─ Controls/     └─ Dialogs/           │
├─────────────────────────────────────────────────────────────┤
│  ViewModels                                                 │
│  ├─ MainViewModel (4 partials)                              │
│  │   ├─ Core          ├─ TabManagement                      │
│  │   ├─ ViewMode      └─ FileOperations                     │
│  ├─ ExplorerViewModel (Miller Columns engine)               │
│  ├─ FolderViewModel   (column with children)                │
│  └─ FileViewModel     (single file)                         │
├─────────────────────────────────────────────────────────────┤
│  Services (40+ classes)                                     │
│  ├─ Core: FileSystemService, SettingsService, IconService   │
│  ├─ FileOps: Copy, Move, Delete, Rename, Compress, Extract  │
│  ├─ Routing: FileSystemRouter → Providers                   │
│  ├─ UI: ContextMenuService, PreviewService, ShellService    │
│  └─ Background: Watcher, Cache, FolderSize, CloudSync, Git  │
├─────────────────────────────────────────────────────────────┤
│  Models                                                     │
│  ├─ IFileSystemItem → DriveItem, FolderItem, FileItem       │
│  └─ TabItem, PathSegment, ConnectionInfo, SearchQuery       │
└─────────────────────────────────────────────────────────────┘
```

---

## Project Structure

```
src/Span/Span/
├── App.xaml.cs              # DI 등록, 아이콘 팩 로드, 멀티 윈도우 관리
├── MainWindow.xaml(.cs)     # 메인 UI + 8개 partial 파일
├── Models/                  # 데이터 모델
│   ├── IFileSystemItem.cs   # 파일/폴더 공통 인터페이스
│   ├── DriveItem.cs         # 드라이브 (사이드바용)
│   ├── FolderItem.cs        # 폴더
│   ├── FileItem.cs          # 파일
│   ├── TabItem.cs           # 탭 상태
│   ├── PathSegment.cs       # 브레드크럼 세그먼트
│   ├── ConnectionInfo.cs    # FTP/SFTP 연결 정보
│   └── SearchQuery.cs       # 검색 쿼리 구조체
├── ViewModels/
│   ├── MainViewModel.cs     # 앱 전체 상태 (+ 3개 partial)
│   ├── ExplorerViewModel.cs # Miller Columns 엔진, 탐색 히스토리
│   ├── FolderViewModel.cs   # 단일 컬럼 (Children, 정렬, 필터, 로딩)
│   ├── FileViewModel.cs     # 단일 파일 (아이콘, 크기, 날짜)
│   └── FileSystemViewModel.cs # 공통 베이스 (IsRenaming, IsSelected 등)
├── Views/
│   ├── DetailsModeView.xaml # Details 뷰 (DataGrid 스타일)
│   ├── ListModeView.xaml    # List 뷰 (컴팩트 목록)
│   ├── IconModeView.xaml    # Icon 뷰 (그리드 썸네일)
│   ├── HomeModeView.xaml    # Home 뷰 (즐겨찾기, 최근 폴더)
│   ├── SettingsModeView.xaml# Settings 탭
│   ├── PreviewPanelView.xaml# 미리보기 패널
│   ├── Controls/            # 커스텀 컨트롤 (AddressBarControl 등)
│   └── Dialogs/             # 대화상자 (BatchRename, Properties 등)
├── Services/
│   ├── FileSystemService.cs # 파일시스템 CRUD
│   ├── FileSystemRouter.cs  # 프로바이더 라우팅
│   ├── SettingsService.cs   # 설정 영속화 (LocalSettings)
│   ├── IconService.cs       # 아이콘 글리프 매핑 (3개 폰트)
│   ├── LocalizationService.cs # 9개 언어 다국어
│   ├── ContextMenuService.cs  # Shell 컨텍스트 메뉴
│   ├── PreviewService.cs     # 파일 미리보기 렌더링
│   ├── ShellService.cs       # Shell 속성, 터미널, 바로가기
│   ├── FileOperations/        # 파일 작업 구현체
│   │   ├── CopyFileOperation.cs
│   │   ├── MoveFileOperation.cs
│   │   ├── DeleteFileOperation.cs
│   │   ├── RenameFileOperation.cs
│   │   ├── CompressOperation.cs
│   │   ├── ExtractOperation.cs
│   │   └── BatchRenameOperation.cs
│   ├── Providers/
│   │   ├── IFileSystemProvider.cs
│   │   ├── LocalFileSystemProvider.cs
│   │   ├── FtpProvider.cs
│   │   └── SftpProvider.cs
│   └── (Background services: Watcher, Cache, FolderSize, CloudSync, Git)
├── Helpers/
│   ├── NativeMethods.cs     # P/Invoke 선언
│   ├── NaturalStringComparer.cs
│   ├── SearchQueryParser.cs
│   ├── DebugLogger.cs
│   └── Converters/          # XAML 값 변환기
└── Assets/
    └── Fonts/               # Remix, Phosphor, Tabler 아이콘 폰트
```

---

## Dependency Injection

`App.xaml.cs::ConfigureServices()` 등록 그래프:

### Core Services (모든 컴포넌트가 의존)

| Service | Lifetime | 역할 |
|---------|----------|------|
| `FileSystemService` | Singleton | 파일/폴더 CRUD, 드라이브 열거, 네트워크 위치(Network Shortcuts) 열거 |
| `SettingsService` | Singleton | LocalSettings 기반 설정 (4개 인터페이스 구현) |
| `LocalizationService` | Singleton | 9개 언어 문자열 리소스 |
| `IconService` | Singleton | 확장자→아이콘 글리프 매핑 (Remix/Phosphor/Tabler) |

### File Operation Services

| Service | Lifetime | 역할 |
|---------|----------|------|
| `FileOperationManager` | Singleton | 동시 실행 엔진 (Copy/Move/Compress/Extract) |
| `FileOperationHistory` | Per-ViewModel (수동 생성) | Undo/Redo 스택 (최대 50), DI 미등록 |

### UI Support Services

| Service | Lifetime | 역할 |
|---------|----------|------|
| `ContextMenuService` | Singleton | Shell 확장 메뉴 빌드 |
| `PreviewService` | Singleton | 파일 미리보기 (Image/Text/PDF/Media) |
| `ShellService` | Singleton | 속성 대화상자, 터미널, 바로가기 |

### Background Services

| Service | Lifetime | 역할 |
|---------|----------|------|
| `FolderContentCache` | Singleton | 폴더 내용 캐시 (LRU) |
| `FileSystemWatcherService` | Singleton | 파일 변경 감지 → UI 갱신 |
| `FolderSizeService` | Singleton | 백그라운드 폴더 크기 계산 |
| `CloudSyncService` | Singleton | OneDrive/Dropbox/GDrive 동기화 상태 |
| `GitStatusService` | Singleton | Git 상태 표시 (modified/staged) |
| `CloudStorageProviderService` | Singleton | 클라우드 드라이브 감지 |

### Infrastructure

| Service | Lifetime | 역할 |
|---------|----------|------|
| `CrashReportingService` | Singleton | Sentry 크래시 리포팅 |
| `JumpListService` | Singleton | Windows 점프 리스트 |
| `NetworkBrowserService` | Singleton | 네트워크 탐색 |
| `ConnectionManagerService` | Singleton | FTP/SFTP 연결 관리/암호화 저장 |

---

## Data Flow

### 폴더 선택 → 컬럼 추가 → 미리보기

```
사용자 클릭 (Miller ListView)
  → FolderViewModel.SelectedChild = item   (x:Bind TwoWay)
  → FolderViewModel.PropertyChanged("SelectedChild")
  → ExplorerViewModel.FolderVm_PropertyChanged()
      ├─ 폴더: HandleFolderSelectionAsync()
      │   → RemoveColumnsFrom(nextIndex)
      │   → AddColumn(new FolderViewModel(folder))
      │   → FolderViewModel.EnsureChildrenLoadedAsync()
      │       → FolderContentCache.TryGet() || FileSystemService.GetChildrenAsync()
      │   → MainWindow.OnColumnsChanged()
      │       → PrepareAndAnimateNewColumn()
      │       → ScrollToLastColumn()
      │
      └─ 파일: HandleFileSelection()
          → ExplorerViewModel.SelectedFile = fileVm
          → MainWindow.SplitPreviewManager
              → UpdatePreviewForSelection() or UpdateInlinePreviewColumn()
```

### 탭 전환

```
탭 클릭
  → MainViewModel.SwitchToTab(index)
      1. SaveActiveTabState()        — Path, ViewMode, Sort 저장
      2. 이전 탭 비활성화             — IsActive=false, columns 리셋
      3. backing field 직접 설정      — _leftExplorer, _currentViewMode (no PropertyChanged cascade)
      4. 새 탭 활성화                 — 분할뷰 상태 복원
  → MainWindow
      5. SwitchMillerPanel(tabId)    — Show/Hide 토글
      6. Switch[Details|List|Icon]Panel()
      7. UpdateViewModeVisibility()
      8. SyncAddressBarControls()
      9. ResubscribeLeftExplorer()
     10. FocusActiveView()
```

### 파일 작업 (Copy/Move)

```
Ctrl+V
  → HandlePaste()
  → 클립보드 StorageItems 추출
  → CheckFileConflictsAsync()          — 충돌 대화상자
  → CopyFileOperation / MoveFileOperation 생성
  → MainViewModel.ExecuteFileOperationAsync()
      → ExecuteViaConcurrentManagerAsync()
          → FileOperationManager.StartOperation()  — 백그라운드 스레드
          → 완료 콜백 → DispatcherQueue.TryEnqueue()
          → RefreshCurrentFolderAsync()
          → RefreshOppositeExplorerAsync()       — 분할뷰 반대쪽
          → LogOperationResult() → ActionLogService
```

### 드래그 앤 드롭

```
내부 드래그 시작
  → OnDragItemsStarting()
  → DataPackage.SetData("DragSourcePaths", paths)
  → StartModifierPollTimer()           — System.Threading.Timer (OLE 모달 루프 대응)

드래그 오버
  → ResolveDragDropMode()             — Ctrl=Copy, Shift=Move, Ctrl+Shift=Link
  → StartSpringLoadTimer()            — 700ms 후 폴더 자동 열기

드롭
  → ExtractDropPaths()                — 내부: DragSourcePaths, 외부: StorageItems
  → HandleDropAsync(paths, dest, mode)
  → ExecuteFileOperationAsync()
```

---

## Core Subsystems

### Miller Columns Engine (`ExplorerViewModel`)

- `ObservableCollection<FolderViewModel> Columns` — 각 컬럼 = 폴더
- 선택 전파: 폴더 선택 → N+1 컬럼 추가, 파일 선택 → N 이후 컬럼 제거
- 컬럼 교체 패턴: 기존 인덱스에 Replace (깜빡임 방지), 범위 초과 시 Add
- 선택 디바운싱: 150ms 딜레이로 빠른 키보드 탐색 시 불필요한 로딩 방지
- `NavigationHistory`: per-Explorer 스택, Back/Forward 지원

### File System Routing (`FileSystemRouter`)

- 경로 프리픽스 기반 프로바이더 디스패치:
  - `C:\`, `D:\` 등 → `LocalFileSystemProvider`
  - `ftp://` → `FtpProvider` (FluentFTP)
  - `sftp://` → `SftpProvider` (SSH.NET)
  - `.zip`, `.7z` → `ArchiveProvider`
- `IFileSystemProvider` 인터페이스: `GetChildrenAsync()`, `CreateFolderAsync()`, `DeleteAsync()`, `RenameAsync()`

### File Operations Pipeline

- `IFileOperation` 인터페이스: `ExecuteAsync(IProgress<OperationProgress>)`, `UndoAsync()`
- 동시 실행: `FileOperationManager` — Copy/Move/Compress/Extract는 백그라운드 스레드
- 순차 실행: `FileOperationHistory` — Delete/Rename/NewFolder는 UI 스레드
- Undo/Redo: `FileOperationHistory` 스택 (최대 50), `ActionLogService`에 기록
- 진행률: `OperationProgress` (바이트, 파일 수, 속도, ETA)

### Tab & Window Management

- **Show/Hide 패턴**: 4개 딕셔너리 (`_millerPanels`, `_detailsPanels`, `_listPanels`, `_iconPanels`) — tabId 키
- **세션 저장**: `SaveTabsToSettings()` → JSON 직렬화 → LocalSettings
- **Tear-off**: `TabStateDto` 직렬화 → `new MainWindow(_pendingTearOff)` → `App.RegisterWindow()`
- **Settings 탭**: Explorer=null, 싱글턴, 세션 저장 제외

### Icon System

- 3개 아이콘 폰트: Remix (기본), Phosphor (대체), Tabler (보조)
- `IconService`: 확장자 → 글리프 매핑, JSON 파일에서 로드 (`icons.json`, `icons-phosphor.json`, `icons-tabler.json`)
- `App.OnLaunched()`: 아이콘 팩 선택에 따라 `Application.Resources` 오버라이드

### Search

- `SearchQueryParser`: AQS 구문 파싱 (`name:`, `ext:`, `size:>1MB`, `date:today`)
- 컬럼 내 필터: `FolderViewModel.ApplyFilter(query)` — 현재 Children 필터링
- 재귀 검색: `RecursiveSearchService.SearchAsync()` — BFS 탐색, Channel 기반 배치 추가, 최대 10K 결과

### Settings Persistence

- `SettingsService`: `Windows.Storage.ApplicationData.Current.LocalSettings`
- 4개 인터페이스: `IAppearanceSettings`, `IBrowsingSettings`, `IToolSettings`, `IDeveloperSettings`
- `OnSettingChanged` 이벤트: 설정 변경 시 UI 실시간 반영 (테마, 폰트, 밀도, 아이콘 스케일 등)

---

## Threading Model

| 스레드 | 용도 | 패턴 |
|--------|------|------|
| UI (DispatcherQueue) | XAML 바인딩, 이벤트 핸들러, 컬렉션 변경 | `DispatcherQueue.TryEnqueue()` |
| ThreadPool | 파일 작업 (Copy/Move), 폴더 크기 계산 | `Task.Run()`, `FileOperationManager` |
| System.Threading.Timer | Modifier 키 폴링 (OLE 드래그 중) | `StartModifierPollTimer()` |

**규칙**:
- `ObservableCollection` 변경은 반드시 UI 스레드에서
- `DispatcherQueue.TryEnqueue(Low)` — 레이아웃 완료 후 포커스/스크롤 작업
- `CancellationTokenSource` — 빠른 탐색 시 이전 로딩 취소
- `_isClosed` 체크 — 윈도우 닫힌 후 DispatcherQueue 콜백 실행 방지

---

## External Dependencies

| Package | Version | 용도 |
|---------|---------|------|
| `Microsoft.WindowsAppSDK` | 1.8 | WinUI 3 프레임워크 |
| `CommunityToolkit.Mvvm` | 8.4.0 | MVVM 인프라 ([ObservableProperty], [RelayCommand]) |
| `CommunityToolkit.WinUI.Controls.Sizers` | 8.2 | GridSplitter 컨트롤 |
| `Microsoft.Extensions.DependencyInjection` | 9.0.2 | DI 컨테이너 |
| `FluentFTP` | 53.0.2 | FTP/FTPS 클라이언트 |
| `SSH.NET` | 2025.1.0 | SFTP 클라이언트 |
| `Sentry` | 6.1.0 | 크래시 리포팅 |
| `System.Security.Cryptography.ProtectedData` | 10.0.3 | 연결 비밀번호 암호화 (DPAPI) |

---

## Deployment

- **Self-contained**: .NET 8 런타임 + WinAppSDK 번들 포함 (런타임 설치 불필요)
- **MSIX 패키징**: `build-msix.bat` → x64/x86/ARM64 3개 아키텍처
- **MS Store**: `.msixupload` → Partner Center 제출
- **GitHub Sideload**: `.msix` + `.cer` + `Install.ps1` → ZIP 배포
- **서명**: 자체 서명 인증서 (`SpanFinder_Dev.pfx`), Store는 재서명
