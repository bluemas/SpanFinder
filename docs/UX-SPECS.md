# UX-SPECS.md

SPAN Finder UX 동작 명세서. AI가 코드를 수정할 때 의도된 동작을 이해하고 깨뜨리지 않기 위한 참조 문서.
모든 명세는 코드 분석 기반이며, 소스 파일 경로를 함께 기재한다.

---

## 1. Preview Panel

> 소스: `MainWindow.SplitPreviewManager.cs`

### 1.1 인라인 미리보기 (Miller Columns 전용)

- **활성 조건**: Miller Columns 모드 + `SettingsService.MillerInlinePreviewEnabled == true` + 좌측 패인(Left pane)만 지원
- **토글**: 미리보기 버튼 클릭 또는 `Ctrl+Shift+P` → `ToggleInlinePreview()` 호출
- **표시 위치**: `MillerTabsHost` Grid 내부, 밀러 컬럼 우측에 배치
- **크기 모델**:
  - 밀러 컬럼: `GridLength(1, GridUnitType.Star)` (남은 공간 전부)
  - 스플리터: `GridLength(6, GridUnitType.Pixel)` — `InlinePreviewSplitter` Border (`ManipulationMode="TranslateX"`)
  - 미리보기: `GridLength(savedWidth, GridUnitType.Pixel)` (고정 픽셀)
  - 저장 키: `LocalSettings["LeftPreviewWidth"]`
  - 기본값: `320px`, 최소값: `200px`, 최대값: `800px`
- **드래그 리사이즈**: `InlinePreviewSplitter` ManipulationDelta → `InlinePreviewCol.Width` 변경 + `LocalSettings["LeftPreviewWidth"]` 즉시 저장
  - `ShowInlinePreview()`는 이미 표시 중이면 return (사용자가 조절한 너비 보존)
- **리사이즈 디바운싱**: `MillerTabsHost.SizeChanged` → 100ms 디바운스 타이머 → `ApplyMillerColumnWidth()`
- **선택 연동**: `ExplorerViewModel.SelectedFile` PropertyChanged → `UpdateInlinePreviewColumn()`
  - 비동기 CancellationTokenSource로 빠른 파일 전환 시 이전 로딩 취소
- **콘텐츠 유형별 처리**:
  - Image: `LoadImagePreviewAsync(path, 512)` → 이미지 + 치수 표시
  - Text: 처음 2000자까지 표시
  - PDF: 첫 페이지 이미지 렌더링
  - Generic: 아이콘 + 메타데이터만
  - Git 정보: 마지막 커밋 (subject, author, relative time) — `GitStatusService.GetLastCommitAsync()`
- **숨김**: `HideInlinePreview()` → 모든 Grid 컬럼 너비 0, Visibility=Collapsed, CTS 취소

### 1.2 사이드 미리보기 (Details/List/Icon 모드)

- **대상**: 좌측 또는 우측 패인의 `PreviewPanelView` 컨트롤
- **토글**: `TogglePreviewForPane(targetPane)` → `IsLeftPreviewEnabled` / `IsRightPreviewEnabled` 토글
- **크기 모델**:
  - Splitter: `GridSplitter Width=2` (Background=Transparent, 드래그 영역만)
  - 패널: `PreviewPanelView` `BorderThickness="1,0,0,0"` (시각적 구분선은 패널 자체에 부착)
  - 패널 너비: `GridLength(GetSavedPreviewWidth(key), GridUnitType.Pixel)`
  - 저장 키: `LocalSettings["LeftPreviewWidth"]` / `LocalSettings["RightPreviewWidth"]`
  - 기본값/최소값: `320px`
- **선택 연동**: 마지막 컬럼의 `SelectedChild` PropertyChanged → `OnLeft/RightColumnSelectionForPreview`
  - 폴더 선택 시: `PreviewShowFolderInfo` 설정이 false면 null 전달 (빈 미리보기)
- **비활성화 시 정리**: `StopMedia()` 호출 + 너비 0으로 설정

### 1.3 분할뷰에서의 미리보기

- **분할뷰 진입 시**: 모든 미리보기를 강제 비활성화
  1. `IsLeftPreviewEnabled = false`, 좌측 미리보기 패널 숨김
  2. `IsRightPreviewEnabled = false`, 우측 미리보기 패널 숨김
  3. `MillerInlinePreviewEnabled = false`, 인라인 미리보기 숨김 + `HideInlinePreview()`
  4. `UpdatePreviewButtonState()` 호출
- **분할뷰 해제 시**: 기본 설정값(`DefaultPreviewEnabled`)으로 복원
  - Miller 모드: 인라인 미리보기만 복원 (사이드 패널 X)
  - Details/List/Icon 모드: 사이드 패널만 복원 (인라인 X)
- **우측 패인**: Miller 모드에서도 인라인 미리보기 인프라 없음 → 항상 사이드 패널 사용

### 1.4 미리보기 토글 시 정리 목록

| 항목 | 정리 동작 |
|------|----------|
| 사이드 패널 미디어 | `PreviewPanelView.StopMedia()` |
| 인라인 CTS | `_inlinePreviewCts.Cancel()` + `Dispose()` |
| 사이드 이벤트 구독 | `UnsubscribePreviewSelection(isLeft)` |
| 인라인 디바운스 타이머 | `_sizeChangedDebounceTimer.Stop()` |
| Grid 컬럼 너비 | Splitter=0, Panel=0 |
| 미리보기 콘텐츠 | `UpdatePreview(null)` |

---

## 2. Split View

> 소스: `MainWindow.SplitPreviewManager.cs` (ToggleSplitView, Focus Tracking)

### 2.1 활성화 시 동작

1. `IsSplitViewEnabled = true`
2. `RightPaneCol.Width = GridLength(1, Star)` → 좌우 1:1 비율
3. 우측 패인 초기화 (설정 `Tab2StartupBehavior` 기반):
   - `0`: Home 모드 (`RightViewMode = ViewMode.Home`)
   - `2`: 사용자 지정 경로 (`Tab2StartupPath`)
   - `1` 또는 fallback: 세션 복원 (`NavigateRightPaneToRealPath()`)
4. `LeftAddressBar` 브레드크럼 동기화
5. `RightExplorer` PropertyChanged 구독 → `SyncRightAddressBar()`
6. 모든 미리보기 비활성화 (1.3 참조)
7. `ActivePane = Right`, `FocusActivePane()`

### 2.2 비활성화 시 동작

1. `IsSplitViewEnabled = false`
2. `RightPaneCol.Width = 0`, `SplitterCol.Width = 0`
3. 우측 미리보기 패널 정리 (미디어 중지, 너비 0)
4. `MainAddressBar` 브레드크럼 동기화
5. `RightExplorer` 구독 해제
6. 미리보기 상태 복원 (1.3 참조)
7. `ActivePane = Left`, `FocusActivePane()`

### 2.3 좌/우 패널 독립성

- **Explorer**: `ViewModel.LeftExplorer` / `ViewModel.RightExplorer` 별도 인스턴스
- **네비게이션 히스토리**: 패인별 독립 Back/Forward 스택
  - 분할뷰 Back/Forward 버튼: `Tag="Right"` 또는 `Tag="Left"`로 대상 결정
- **뷰 모드**: `ViewModel.LeftViewMode` / `ViewModel.RightViewMode` 독립
- **정렬/필터**: 패인별 독립 (Sort 메뉴 opening 시 `ActivePane` 먼저 설정)
- **AddressBar**: `MainAddressBar` (비분할), `LeftAddressBar` / `RightAddressBar` (분할)
  - `ResolveExplorerForAddressBar()`: sender 참조로 대상 Explorer 결정

### 2.4 포커스 추적

- `OnLeftPaneGotFocus` / `OnRightPaneGotFocus`: `ActivePane` 전환 (GotFocus 이벤트)
- `OnLeft/RightPanePointerPressed`: 빈 영역 클릭 시 보완 (GotFocus가 발생하지 않는 영역)
  - 패인 헤더 내 버튼 클릭 시 `FocusActivePane()` 생략 → Button Click 이벤트 보존
- `Ctrl+Tab`: 분할뷰에서 좌/우 패인 포커스 전환

### 2.5 툴바 항목 이동 규칙

- **비분할 모드**: 상단 단일 툴바 표시 (`IsSingleNonHomeVisible`)
  - Back/Forward/Up, 주소바, NewFolder, Cut/Copy/Paste, Rename, Delete, Sort, ViewMode, Split, Preview
- **분할 모드**: 상단 통합 바 숨김, 각 패인 헤더에 축소 툴바 표시
  - 각 패인: Back/Forward/Up, CopyPath, Sort, ViewMode, Preview
  - 글로벌 버튼(NewFolder, NewFile, Delete 등)은 활성 패인 기준으로 동작
- **Separator**: 분할뷰 하단 border `UnifiedBarBorderThickness` → `IsSplitViewEnabled ? 0 : (0,0,0,1)`
- **활성 패인 시각 표시**: 좌/우 패인 하단 accent bar (`LeftPaneAccentOpacity` / `RightPaneAccentOpacity`)

---

## 3. Tab Management

> 소스: `MainWindow.TabManager.cs`, `MainWindow.xaml.cs`

### 3.1 탭별 독립 패널 (Show/Hide 딕셔너리)

```
_tabMillerPanels  : Dictionary<string, (ScrollViewer, ItemsControl)>
_tabDetailsPanels : Dictionary<string, DetailsModeView>
_tabIconPanels    : Dictionary<string, IconModeView>
_tabListPanels    : Dictionary<string, ListModeView>
```

- **패턴**: 탭 ID를 키로 사용. 활성 탭 패널만 `Visibility.Visible`, 나머지 `Collapsed`
- **생성**: `CreateMillerPanelForTab(tab)` → 새 ScrollViewer + ItemsControl 쌍 생성 후 딕셔너리에 추가
- **전환**: `SwitchMillerPanel(tabId)` → 이전 활성 패널 Collapsed, 새 활성 패널 Visible
- **제거**: `RemoveMillerPanel(tabId)` → 딕셔너리에서 제거 + UI 트리에서 제거
- **효과**: 탭 전환 시 재렌더링 없이 즉시 전환 (스크롤 위치, 선택 상태 보존)

### 3.2 탭 전환 시 보존해야 할 상태

| 상태 | 보존 방식 |
|------|----------|
| Miller Columns 스크롤 위치 | 패널 Show/Hide로 자동 보존 |
| 선택된 항목 | ExplorerViewModel 인스턴스 유지 |
| 뷰 모드 (Miller/Details/Icon/List) | `TabItem.ViewMode` 저장 |
| 브레드크럼 경로 | `ExplorerViewModel.PathSegments` 유지 |
| 정렬 상태 | FolderViewModel에 유지 |
| 미리보기 상태 | `ViewModel.SavePreviewState()` |
| Explorer 인스턴스 | `TabItem.Explorer` 직접 참조 |

### 3.3 탭 전환 추가 동작

- `ResubscribeLeftExplorer()`: 좌측 Explorer PropertyChanged 재구독
- `ResubscribeInlinePreview()`: 인라인 미리보기 Explorer 재구독
- `ResubscribeGitStatusBar()`: Git 상태바 Explorer 재구독
- `UpdateViewModeVisibility()`: 뷰 모드에 맞는 패널 표시
- 탭 표시명: Home 모드 → `_loc.Get("Home")`, 아니면 폴더명

### 3.4 탭 떼어내기(tear-off)

1. 탭 드래그 시작: `PointerPressed` → `_tabDragStartPoint` 저장
2. 드래그 임계값: `TAB_DRAG_THRESHOLD = 8px` 초과 시 떼어내기 시작
3. `TabStateDto` 생성: 경로, 뷰모드, 정렬, 스크롤 등 직렬화
4. 새 `MainWindow` 생성: `_pendingTearOff = dto`
5. 원본 윈도우에서 탭 제거
6. 새 윈도우 `Loaded`에서 `_pendingTearOff` 소비 → 세션 복원
7. `_isTearOffWindow = true` → 세션 저장에서 제외

### 3.5 설정 탭 특수 처리

- 최대 1개만 허용
- `ViewMode = ViewMode.Settings`, `Explorer = null`
- 세션 저장에서 제외
- `Ctrl+,` 또는 사이드바 설정 버튼으로 열기
- `Ctrl+W` 또는 `Escape`로 닫기
- ActionLog 탭도 동일 패턴 (`ViewMode.ActionLog`)

---

## 4. Navigation

> 소스: `ExplorerViewModel.cs`, `MainWindow.NavigationManager.cs`

### 4.1 Miller Columns 선택 → 컬럼 추가/제거 규칙

- **폴더 선택**: Column N에서 폴더 선택 → Column N+1 생성 (기존 N+1 이후 컬럼 제거)
  - `FolderVm_PropertyChanged` → `HandleFolderSelectionAsync()`
  - 교체 패턴: 기존 Column N+1이 있으면 교체 (깜빡임 방지), 없으면 추가
  - 교체 전 반드시 이전 ViewModel의 `PropertyChanged` 구독 해제
- **파일 선택**: Column N+1 이후 모든 컬럼 제거 + `SelectedFile` 설정
- **선택 디바운싱**: `_selectionDebounceChannel` (150ms) — 빠른 키보드 탐색 시 중간 폴더 로딩 방지
- **자동 네비게이션**: `EnableAutoNavigation` 플래그로 제어
  - `NavigateTo()` 중 false로 설정 → 로딩 완료 + 400ms 후 복원

### 4.2 NavigateTo vs NavigateToPath vs NavigateToSegment

| 메서드 | 용도 | 동작 |
|--------|------|------|
| `NavigateTo(FolderItem)` | 단일 폴더로 전체 리셋 | 모든 컬럼 정리 → 루트 컬럼 1개 생성 → 로딩 |
| `NavigateToPath(string)` | 경로 문자열로 계층 구축 | 루트부터 대상까지 전체 컬럼 계층 생성 (최대 8개). 35단계 이상이면 마지막 8개만 표시 |
| `NavigateToSegment(PathSegment)` | 브레드크럼 클릭 | 현재 컬럼에 해당 경로가 있으면 truncate (하위 제거). 없으면 `NavigateToPath` fallback |
| `NavigateIntoFolder(FolderViewModel)` | 더블클릭 (Details/Icon) | 부모 컬럼 기준으로 다음 컬럼 교체/추가 |
| `NavigateUp()` | 상위 폴더 이동 | 로컬: `Path.GetDirectoryName()`. 원격: URI 마지막 세그먼트 제거 |

### 4.3 NavigateToPath 상세

1. `\\?\` 접두사 제거
2. `archive://` 경로 → `NavigateToArchivePath()` 분기
3. 원격 경로 → `NavigateToRemotePath()` 분기
4. UNC 경로: `Task.Run(() => Directory.Exists())` (30초+ 블로킹 방지)
5. 경로 정규화: `Path.GetFullPath()` (PathTooLongException 가드)
6. 히스토리 Push
7. 세그먼트 분해: root + relative parts
8. 깊은 경로 최적화: `MaxMillerColumns = 8` 초과 시 마지막 8개만
9. Phase 1: FolderViewModel 배열 생성 (I/O 없음)
10. Phase 2: 모든 컬럼 즉시 UI 추가 (ProgressRing 즉시 표시)
11. Phase 3: `Task.WhenAll()` 병렬 로딩
12. Phase 4: SelectedChild를 실제 Children 인스턴스로 재매칭

### 4.4 브레드크럼 클릭 동작

- `OnAddressBarBreadcrumbClicked()` → `explorer.NavigateToPath(segment.FullPath)`
- `::home::` 경로 → `ViewMode.Home`으로 전환
- Chevron 클릭 → 서브폴더 드롭다운 (`ShowBreadcrumbChevronFlyout`)
  - 현재 탐색 중인 하위 폴더에 체크마크 아이콘 표시
  - 폴더 클릭 → `NavigateToPath()`

### 4.5 뒤로/앞으로 동작

- **히스토리 구조**: `_backStack` / `_forwardStack` (Stack<string>)
- **Push 규칙**: `PushToHistory()` — 중복 방지 (현재 경로와 같으면 스킵)
- **GoBack()**: `_backStack.Pop()` → 현재 경로를 `_forwardStack.Push()` → `NavigateToPath()`
  - 경로가 더 이상 존재하지 않으면 다음 항목 시도 (재귀)
  - `_isNavigatingHistory = true` → `NavigateTo` 내 중복 Push 방지
- **GoForward()**: GoBack의 역방향 동일 패턴
- **히스토리 드롭다운**: 뒤로/앞으로 버튼 우클릭 → `ShowHistoryDropdown()`
  - 최대 15개 항목, 현재 위치 체크마크 + 볼드
  - `NavigateToBackHistoryEntry(index)` / `NavigateToForwardHistoryEntry(index)`
- **마우스**: XButton1 = 뒤로, XButton2 = 앞으로
- **키보드**: `Alt+Left` = 뒤로, `Alt+Right` = 앞으로, `Alt+Up` = 상위

---

## 5. Keyboard Shortcuts

> 소스: `MainWindow.KeyboardHandler.cs`

### 5.1 전역 단축키 (OnGlobalKeyDown)

| 단축키 | 동작 | 비고 |
|--------|------|------|
| `Ctrl+C` | 복사 | TextBox 포커스 시 네이티브 처리에 위임 |
| `Ctrl+X` | 잘라내기 | 〃 |
| `Ctrl+V` | 붙여넣기 | 〃 |
| `Ctrl+Shift+V` | 바로가기로 붙여넣기 | |
| `Ctrl+A` | 전체 선택 | 〃 |
| `Ctrl+Shift+A` | 선택 해제 | |
| `Ctrl+I` | 선택 반전 | |
| `Ctrl+Z` | 실행 취소 | |
| `Ctrl+Y` | 다시 실행 | |
| `Ctrl+D` | 선택 항목 복제 | |
| `Ctrl+L` | 주소바 편집 모드 | Home에서도 동작 (MillerColumns 전환 후) |
| `Ctrl+F` | 검색 포커스 | |
| `Ctrl+Shift+F` | 필터 바 토글 | |
| `Ctrl+N` | 새 창 | |
| `Ctrl+Shift+N` | 새 폴더 | |
| `Ctrl+T` | 새 탭 | |
| `Ctrl+W` | 탭 닫기 | Settings/ActionLog 탭이면 해당 탭 닫기 |
| `Ctrl+Enter` | 선택 폴더를 새 탭으로 열기 | |
| `Ctrl+Tab` | 분할뷰 좌/우 패인 전환 | |
| `Ctrl+Shift+E` | 분할뷰 토글 | |
| `Ctrl+Shift+P` | 미리보기 토글 | |
| `Ctrl+H` | 숨김 파일 토글 | |
| `Ctrl+1` | Miller Columns 뷰 | |
| `Ctrl+2` | Details 뷰 | |
| `Ctrl+3` | List 뷰 | |
| `Ctrl+4` | Icon 뷰 (마지막 크기) | |
| `` Ctrl+` `` | 터미널 열기 | |
| `Ctrl+'` | 터미널 열기 (대체) | |
| `Ctrl+,` | 설정 탭 | |
| `Ctrl+Shift+=` | 모든 컬럼 너비 균등화 (220px) | Miller 전용 |
| `Ctrl+Shift+-` | 모든 컬럼 내용 자동 맞춤 | Miller 전용 |
| `Alt+Left` | 뒤로 | |
| `Alt+Right` | 앞으로 | |
| `Alt+Up` | 상위 폴더 | |
| `Alt+D` | 주소바 포커스 (Explorer 호환) | |
| `Alt+Enter` | 속성 대화상자 | |
| `F1` / `Shift+?` | 도움말 오버레이 토글 | |
| `F2` | 이름 변경 | |
| `F3` | 검색 포커스 | |
| `F4` | 주소바 편집 모드 | |
| `F5` | 새로 고침 | |
| `F11` | 전체 화면 토글 | |
| `Delete` | 휴지통으로 삭제 | |
| `Shift+Delete` | 영구 삭제 | |
| `Escape` | 잘라내기 상태 해제 / Quick Look 닫기 | |

### 5.2 설정/Home/ActionLog 모드 제한

- 파일 조작 단축키(Delete, F2, F5 등) 차단
- 허용: 뷰 전환(Ctrl+1~4), 탭(Ctrl+T/W), 주소바(Ctrl+L), 숨김 파일(Ctrl+H), 새 창(Ctrl+N), 터미널, 설정
- `Escape`: Settings → 설정 탭 닫기, ActionLog → 로그 탭 닫기

### 5.3 Miller 전용 단축키 (OnMillerKeyDown)

| 키 | 동작 |
|----|------|
| `Right` | 선택 폴더의 자식 컬럼으로 포커스 이동. 자식 컬럼 없으면 생성 |
| `Left` / `Backspace` | 부모 컬럼으로 포커스 이동 |
| `Enter` | 폴더: Right 동작. 파일: 실행 (아카이브: 내부 진입) |
| `Home` | 현재 컬럼 첫 항목 선택 |
| `End` | 현재 컬럼 마지막 항목 선택 |
| `Space` | QuickLook 활성 시 미리보기 창, 비활성 시 type-ahead |
| 문자 키 | Type-ahead 검색 (800ms 버퍼, 대소문자 무시 prefix 매칭) |

### 5.4 한국어 키보드 특수 처리 (ScanCode fallback)

- **문제**: 한국어 키보드에서 VK_OEM 코드가 다른 VirtualKey로 매핑될 수 있음
- **해법**: `default` 분기에서 물리 키 ScanCode로 판별
  - ScanCode `41` (backtick 위치) → 터미널 열기
  - ScanCode `40` (single quote 위치) → 터미널 열기
  - ScanCode `51` (comma 위치) → 설정 열기
- **적용 위치**: `OnGlobalKeyDown()` Ctrl 분기 + Settings/Home 차단 로직

### 5.5 인라인 리네임 중 키 처리

- **리네임 진행 중**: `IsRenaming == true`
  - 전역 단축키: `F2`만 허용 (선택 영역 순환), 나머지 차단
  - Miller 키보드: 전체 차단
- **리네임 직후**: `_justFinishedRename = true`
  - `OnMillerKeyDown()` 진입 시 플래그 확인 → 해당 이벤트 소비 후 `false`로 리셋
  - 목적: Enter로 리네임 커밋 후 동일 Enter가 파일 실행으로 이어지는 것 방지
- **빈 영역 클릭**: `OnGlobalPointerPressed` 좌클릭 → `CancelAnyActiveRename()` (리네임 TextBox 내부 클릭은 제외)

### 5.6 Type-ahead 검색

- 2경로 처리:
  1. `HandleTypeAhead()` (KeyDown): Latin 문자 A-Z, 0-9 → `KeyToChar()` 변환
  2. `OnMillerCharacterReceived()` (CharacterReceived): 비라틴 문자 (한글/일본어/중국어)
- `_typeAheadHandledInKeyDown` 플래그로 중복 방지
- 버퍼 초기화: `DispatcherTimer` (기본 800ms)
- 매칭: `StartsWith(buffer, OrdinalIgnoreCase)` → 첫 매칭 항목 선택 + `ScrollIntoView`

---

## 6. File Operations

> 소스: `MainWindow.FileOperationHandler.cs`, `Services/FileOperations/`

### 6.1 복사 흐름 (CopyFileOperation)

1. `HandleCopy()` → 선택 항목 경로를 `_clipboardPaths`에 저장
2. `HandlePaste()` → 대상 폴더 결정 (포커스 컬럼 또는 마지막 컬럼의 부모)
3. 충돌 확인: 대상 경로에 동일 이름 존재 시
   - `_applyToAll == false` (첫 충돌): `GetUniqueFileName()` 자동 번호 부여
   - `_applyToAll == true`: `ConflictResolution`에 따라 처리
4. `CopyFileOperation.ExecuteAsync()` → 파일별 바이트 기반 진행률 보고
5. 원격 경로: `FileSystemRouter` → stream 기반 복사 (FTP/SFTP 지원)
6. Undo: 복사된 파일 삭제 (로컬만, 원격 `CanUndo = false`)

### 6.2 이동 흐름 (MoveFileOperation)

- 같은 볼륨: `File.Move()` / `Directory.Move()` (빠른 이동)
- 크로스 볼륨/원격: stream 기반 복사 + 원본 삭제 (일시정지 지원)
- Undo: 역방향 이동 (로컬만)

### 6.3 삭제 흐름 (DeleteFileOperation)

- **Delete**: `SHFileOperation` + `FOF_ALLOWUNDO` → 휴지통
  - 실패 시: Reserved device name → 영구 삭제
  - ACCESS_DENIED → 관리자 권한 PowerShell 실행 (UAC 프롬프트)
- **Shift+Delete**: `DeleteFileW` + `\\?\` 접두사 → 영구 삭제
  - ACCESS_DENIED → 관리자 권한 `Remove-Item`
- **원격 경로**: `provider.DeleteAsync()` (FTP/SFTP)
- **Undo** (휴지통만):
  - `Shell.Application` COM → `NameSpace(10)` (Recycle Bin)
  - `GetDetailsOf(item, 1)` = "Original Location" 매칭
  - `Folder.MoveHere(item, 0x0014)` 로 복원

### 6.4 리네임 흐름

1. `F2` → `HandleRename()` → 대상 `FileSystemViewModel.BeginRename()`
2. `IsRenaming = true` → XAML 바인딩으로 TextBox 표시
3. `Enter` → `CommitRename()`: `FileSystemService.RenameAsync()` 호출
   - `_justFinishedRename = true` 설정
4. `Escape` → `CancelRename()`: 원래 이름 복원
   - `_justFinishedRename = true` 설정
5. `LostFocus` → `CommitRename()` (자동 커밋)
6. F2 반복: 선택 영역 순환 (이름 전체 → 확장자 제외 → 확장자만)

### 6.5 충돌 처리 (ConflictResolution)

| 전략 | 동작 |
|------|------|
| `Prompt` | 기본값. 첫 충돌 시 자동 번호 부여 (`GetUniqueFileName`) |
| `Replace` | 기존 파일 삭제 후 덮어쓰기 |
| `Skip` | 해당 파일 건너뛰기 |
| `KeepBoth` | 번호 부여하여 양쪽 유지 |
| `_applyToAll` | true면 이후 모든 충돌에 같은 전략 적용 |

### 6.6 Undo/Redo

- `FileOperationHistory`: `_undoStack` / `_redoStack` (Stack<IFileOperation>)
- 최대 히스토리: `MaxHistorySize = 50`
- 실행 성공 + `CanUndo == true` → `_undoStack.Push()`
- Undo 실행 시 `_redoStack.Push()`, 실패 시 `_undoStack`에 복원
- Redo 실행 시 `_undoStack.Push()`, 실패 시 `_redoStack`에 복원
- 단축키: `Ctrl+Z` (Undo), `Ctrl+Y` (Redo)
- Undo 불가 조건: 영구 삭제, 원격 파일 작업

### 6.7 진행률 표시 (FileOperationProgress)

- `CurrentFile`: 현재 처리 중인 파일명
- `ProcessedBytes` / `TotalBytes`: 바이트 기반 진행률
- `Percentage`: 명시 설정 또는 `ProcessedBytes * 100 / TotalBytes` 자동 계산
- `SpeedBytesPerSecond`: 전송 속도
- `EstimatedTimeRemaining`: 예상 남은 시간
- `CurrentFileIndex` / `TotalFileCount`: 파일 단위 진행률
- 일시정지: `IPausableOperation.SetPauseEvent(ManualResetEventSlim)` → I/O 청크 사이에서 대기

---

## 7. Context Menu

> 소스: `Services/ContextMenuService.cs`

### 7.1 셸 컨텍스트 메뉴 통합

- **비동기 로딩**: `BuildFileMenuAsync()` / `BuildFolderMenuAsync()` → 커스텀 항목 즉시 표시 + 셸 확장 비동기 추가
- **셸 확장 재표시**: `RebuildMenuWithShellExtensionsAsync()` → 현재 메뉴 닫고 셸 확장 포함 버전으로 재빌드
- **세션 관리**: `ShellContextMenu.Session` — 메뉴 열려있는 동안 유지, 닫힐 때 해제
- **필터링 설정**:
  - `ShowWindowsShellExtras`: Share, Send to, Pin to Start, Include in library 등 숨김/표시
  - `ShowCopilotMenu`: Copilot 메뉴 항목 숨김/표시
  - Developer 항목: Git GUI, VS Code, TortoiseGit 등 별도 필터링
- **Edit With 그룹핑**: "Edit with X" 패턴 항목이 2개 이상이면 서브메뉴로 그룹화

### 7.2 번역 시스템

- **Verb 기반**: `ShellVerbTranslations[lang][verb]` → 캐노니컬 verb를 로컬 텍스트로
- **Text 기반**: `ShellTextTranslations[lang][englishText]` → 영문 텍스트를 번역
- **지원 언어**: ko, ja (verb + text 기반 번역 테이블)

### 7.3 AccessKey 처리

- **WinUI 3 제약**: `AccessKey` 속성 설정 시 visual tree 미연결 상태에서 `E_INVALIDARG` 발생
- **대안**: `Tag`에 action 저장 → `TryInvokeAccessKey(key)`에서 단축키 처리
- **단독 키 입력**: `OnGlobalKeyDown()` → 컨텍스트 메뉴 열려있으면 → A-Z/0-9 키를 `TryInvokeAccessKey()`에 전달
  - Ctrl 키가 눌려있으면 무시 (Ctrl+C 등은 글로벌 핸들러에서 처리)
- **AccessKey 추출**: `ExtractAccessKeyFromText("복사(C)")` → `"C"`
  - 괄호 패턴: `텍스트(키)` → 키 추출
- **CJK 주의사항**: `WindowsShellExtraTexts` 필터 매칭 시 CJK 괄호 strip으로 항목이 사라질 수 있음 → `ApplyCompact` 방어만 적용

### 7.4 서브메뉴 열기 패턴

- WinUI 3에 `SubMenuFlyout.Open()` API 없음
- **대안**: `Focus()` + `keybd_event(VK_RIGHT)` 시뮬레이션
- `_openedSubItem`: 현재 열린 서브메뉴 추적 → AccessKey 탐색 범위 결정

---

## 8. Search

> 소스: `MainWindow.FileOperationHandler.cs` (Search Box), `Helpers/SearchQueryParser.cs`, `Services/RecursiveSearchService.cs`, `Models/SearchQuery.cs`

### 8.1 검색 구문 (Advanced Query Syntax)

| 필터 | 구문 | 예시 |
|------|------|------|
| 파일 종류 | `kind:<type>` | `kind:image`, `kind:document`, `kind:video`, `kind:audio`, `kind:archive`, `kind:code`, `kind:exe`, `kind:font` |
| 파일 크기 | `size:<op><value><unit>` | `size:>1MB`, `size:<100KB`, `size:empty`, `size:large`, `size:huge` |
| 수정 날짜 | `date:<preset>` 또는 `date:<op><yyyy-MM-dd>` | `date:today`, `date:thisweek`, `date:>2024-01-01` |
| 확장자 | `ext:<ext>` | `ext:.pdf`, `ext:jpg;png;gif` (다중) |
| 이름 | 평문 텍스트 | `report` (contains), `*.txt` (와일드카드) |

- **다중 필터**: AND 로직으로 결합
- **따옴표**: `"my file"` → 공백 포함 단일 토큰
- **와일드카드**: `*` → `.*`, `?` → `.` (정규식 변환, 전체 이름 매칭)
- **크기 프리셋**: empty(=0), tiny(<16KB), small(<1MB), medium(>=1MB), large(>128MB), huge(>1GB)
- **날짜 프리셋**: today, yesterday, thisweek, lastweek, thismonth, lastmonth, thisyear, lastyear

### 8.2 검색 실행 흐름

1. `SearchBox`에 쿼리 입력 + `Enter`
2. `SearchQueryParser.Parse(text)` → `SearchQuery` 객체 생성
3. 검색 루트: `explorer.Columns[0].Path` (첫 컬럼 = 네비게이션 루트)
4. `ExplorerViewModel.StartRecursiveSearchAsync(query, rootPath, showHidden)` 호출
5. `RecursiveSearchService.SearchInBackground()`:
   - BFS 방식 디렉토리 순회 (백그라운드 스레드)
   - `Channel<List<FileSystemViewModel>>` 으로 배치(50개씩) 결과 전달
   - 최대 결과: `MaxResults = 10,000`
6. UI 스레드에서 배치 수신 → 결과 컬럼에 추가

### 8.3 검색 결과 표시

- 검색 결과는 별도 "검색 결과" 컬럼으로 표시 (`IsRecursiveSearching = true`)
- 검색 중: 진행 상태 텍스트 (`SearchStatusText`) — 발견 수 / 스캔 폴더 수
- 완료 시: 결과 수 표시. 10,000개 초과 시 제한 메시지
- 결과 항목: 전체 경로 표시

### 8.4 검색 클리어 동작

- `Escape` in SearchBox:
  1. 재귀 검색 활성 (`HasActiveSearchResults`): `CancelRecursiveSearch()` → 이전 컬럼 복원
  2. 인라인 필터 활성 (`_isSearchFiltered`): `RestoreSearchFilter()` → 원래 Children 복원
  3. `SearchBox.Text = ""` + 탐색기 포커스 복원
- `CancelRecursiveSearch()`: CTS 취소 + 저장된 이전 컬럼 상태 복원 (`restoreColumns: true`)

---

## 9. Localization

> 소스: `Services/LocalizationService.cs`, `Services/LocalizationData.cs`

### 9.1 지원 언어 (9개)

| 코드 | 언어 |
|------|------|
| `en` | English |
| `ko` | 한국어 |
| `ja` | 日本語 |
| `zh-Hans` | 简体中文 |
| `zh-Hant` | 繁體中文 |
| `de` | Deutsch |
| `es` | Español |
| `fr` | Français |
| `pt-BR` | Português (Brasil) |

### 9.2 언어 해석 순서

1. 설정에서 `"system"` → `ResolveSystemLanguage()` 호출
2. `CultureInfo.CurrentUICulture` 기반:
   - `zh`: `zh-CN/zh-Hans-*` → `zh-Hans`, `zh-TW/zh-HK/zh-MO` → `zh-Hant`
   - `pt` → `pt-BR` (고정)
   - 기타 2글자 코드: 지원 여부 확인 후 `"en"` fallback
3. `ApplicationLanguages.PrimaryLanguageOverride` 설정: 시스템 대화상자 언어도 동기화

### 9.3 사용 패턴

| 패턴 | 용도 | 예시 |
|------|------|------|
| `_loc.Get(key)` | ViewModel/View에서 인스턴스 메서드 | `_loc.Get("Home")` |
| `LocalizationService.L(key)` | Service 레이어 static 헬퍼 | `L("Op_CopySingle")` — DI 없이 접근 |

- **`_loc.Get(key)`**: 현재 언어 → 영어 fallback → 키 반환
- **`L(key)`**: `App.Current.Services`에서 인스턴스 가져오기 시도 → 실패 시 영어 fallback

### 9.4 동적 문자열 format placeholder

- `string.Format(L("Op_CopySingle"), fileName, destName)` 패턴 사용
- `LocalizationData.cs`에서 `{0}`, `{1}` 등 placeholder 포함:
  ```
  ("Op_CopySingle", "Copy '{0}' to '{1}'", "'{0}'을(를) '{1}'에 복사", ...)
  ("Op_CopyMultiple", "Copy {0} items to '{1}'", "{0}개 항목을 '{1}'에 복사", ...)
  ```
- **규칙**: placeholder 수와 순서는 모든 언어에서 동일해야 함

### 9.5 런타임 언어 전환

1. `LocalizationService.Language` setter → `LanguageChanged` 이벤트 발생
2. `MainWindow` 구독 → `LocalizeViewModeTooltips()` 호출
3. 모든 하드코딩 XAML 텍스트 재설정:
   - 툴바 툴팁, 사이드바 라벨, 정렬/뷰모드 메뉴, 탭 헤더, 검색 placeholder
4. 앱 재시작 불필요 (런타임 즉시 반영)

---

## 10. Network Shortcuts (네트워크 위치)

> 소스: `Services/FileSystemService.cs`, `ViewModels/MainViewModel.cs`, `MainWindow.xaml.cs`

### 10.1 네트워크 위치 열거

- **소스 경로**: `%APPDATA%\Microsoft\Windows\Network Shortcuts` 하위 폴더
- **대상 추출**: `ResolveNetworkShortcutTarget()` — 3단계 fallback
  1. `target.lnk` → Shell Link 바이너리 파싱 (UNC 경로 + CommonNetworkRelativeLink)
  2. `target.lnk` → Unicode FTP/HTTP URL 패턴 검색 (`ExtractUrlFromLnkBytes()`)
  3. `desktop.ini` → `URL=` 라인 파싱
- **target 없는 폴더**: 폴더명 자체를 이름으로 표시 (Windows 탐색기와 동일)
- **사이드바 표시**: `NetworkAndRemoteDrives` 컬렉션에 추가, 네트워크 섹션에 표시

### 10.2 네트워크 드라이브 이름

- **매핑 드라이브** (예: Y:): `WNetGetConnectionW` P/Invoke로 UNC 경로 추출 → `share (\\server\path) (Y:)` 형식
- **비매핑 바로가기**: 폴더명 + target 경로 → `211.60.125.26 (ftp://lg@211.60.125.26)` 형식

### 10.3 FTP 바로가기 연결

- **클릭 시 흐름**:
  1. `OpenDrive()` → `ftp://` URL 감지 → `NetworkShortcutFtpRequested` 이벤트
  2. `SavedConnections`에서 같은 호스트+포트 매칭 → 있으면 기존 연결 사용
  3. 없으면 → `ShowConnectionDialog(prefilled)` (호스트/유저명/포트 미리 채움) → 비밀번호 입력 → 저장 + 연결
- **잠금 뱃지**: `NeedsAuth = true`인 FTP 바로가기 아이콘 우하단에 🔒 뱃지 (7px, Caution 색상)
  - 저장된 연결이 있으면 `NeedsAuth = false` → 뱃지 사라짐
- **중복 방지**: `RebuildAllDrives()`에서 네트워크 바로가기 호스트와 같은 SavedConnections 제외

### 10.4 연결 해제

- **매핑 드라이브**: `WNetCancelConnection2W` (기존 로직)
- **네트워크 바로가기**: `DeleteNetworkShortcutFolder()` — ReadOnly/System 속성 해제 후 폴더 삭제
  - 3단계 fallback: `NetworkShortcutPath` 직접 → UNC 경로로 검색 → `WNetCancelConnection2`

### 10.5 자동 동기화

- `FileSystemWatcher` on `Network Shortcuts` 폴더 (`NotifyFilter.DirectoryName`)
- Created/Deleted/Renamed → `RefreshDrives()` 자동 호출
- 창 닫힘 시 Watcher dispose
