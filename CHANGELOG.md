# Changelog

## 1.0.1 — 2026-10-05

- Native GDI held-frame handoff: synchronous paint/DWM flush before outgoing decoder exit, bounded 2,073,600-pixel bridge, retained on replacement failure.
- Per-display renderer ownership across app copies/custom libraries. Detect old 1.0.0 workers, recover abandoned ownership, report EN-VI conflicts without retry loops.
- Supplied empty-box/laptop/wallpaper-card mascots; transparent library exterior and bounded 384-pixel UI decode.
- Consistent version labels, source-only Git rules, per-user Setup/Portable packaging, public README and technical continuation context.
- Existing schema, EXE, data and startup identifiers preserved. Detailed architecture and open physical/soak QA: docs/DEVELOPMENT.md.
## 1.0.0 — 2026-10-05

Product TD-WallpaperEngine; existing schema 1, internal EXE, data and startup identifiers preserved. Includes unreleased lifecycle 0.4.5 work and requested Chặng 4 milestone.

- Startup.LaunchDelay 15→3 seconds on --startup only; manual/worker zero. Portable first data 3195.68 ms vs manual 1070.61 ms including cold runtime overhead; not a cold-boot benchmark.
- BrandCropExtension uses exact owner sheet cells; smaller stickers and derived ICO.
- StarterPack: selected owner videos converted to ≤1080p/30 muted H.264, two credited fantasy stills. Seed only new libraries through existing copy/import; explicit Add samples deduplicates.
- LibraryUtilities/MainWindow.Tools: health/validate/reviewed relink, live stale/kind/duplicate checks, Save rollback preserves references.
- PerformanceProfiles/AppRule: release/resume owned player on automatic policy, preserve current/pin/clock, active-window hook/poll. Additive fields normalize/deep-copy and roll back on failed Save.
- DownloadQueue: serial background jobs, bounded checkpoint records, yt-dlp quality caps on all fallbacks, progress phases, dedup/cancel/retry, late-progress guard and original import transaction.
- DownloadToolRunner: private Windows kill-on-close job plus cancellation process-tree kill/wait. ArgumentList/no shell unchanged.
- ToolsetManager: official asset digest required, bounded streaming, pre-execution SHA 256/version check, atomic active/previous pointer, packaged fallback and rollback. mpv/FFmpeg remain pinned.
- Diagnostics strips wallpaper names/URLs and records bounded backend/resource/tool versions without copying personal data.
- Backup: detached snapshot, disk streams, partial cleanup, traversal-safe bounded restore to generated owned media; confirmation before extraction. Storage shows media/cache, quarantines unused owned files, supports Undo.
- UX: visible library tools sidebar, shared dialog style, ENVI, accessible icon names, keyboard focus and compact regression.
- Validation: build/publish 0 warnings 0 errors, core 23/23; native queue/profile/updater/starter and MP 4/WebM/QoL/ENVI/drop/rollback/controller/diagnostics/transitions passed; portable smoke 5/5. Decoder 228.79 MiB→owned exit; hardware D3D11VA observed.
- Delivery: portable/source ZIPs, source/file/tool manifests and verified entry hashes, checksums, workflow, credits/licenses, logs and handoff.
- Open physical boot/game/mixed DPI/two-monitor/sleep/lock/Explorer/hotplug and 8/24 h soak. Version 1.0.0 does not certify unrun tests. No GitHub repo/push created.

## Historical development notes
The following unreleased 0.4.5 entries describe earlier development measurements, including the superseded 15-second delay.


## Chưa phát hành — 0.4.5, Chặng 3 diagnostics + controller/IPC/cleanup/event + startup (04/10/2026)

- Startup thêm --startup và chờ 15 giây bằng Task.Delay trước khi đọc thư viện hoặc tạo UI/thumbnail/decoder. Manual launch và wallpaper worker vào ngay. Settings giải thích bằng EN/VI. Normal launch bản mới refresh entry đã bật; QA không sửa startup cá nhân. Thời gian tính từ lúc Windows gọi app tại đăng nhập.
- Menu Thông tin bộ phát / Player diagnostics hiển thị codec, kích thước, FPS nguồn/sau bộ lọc, hwdec-current thực tế và CPU fallback, dropped/mistimed frames, thời gian loaded/frame đầu, PID/RAM/handles/CPU interval. Giá trị không có giữ null, không giả 0. Parser đọc hwdec option MPV dạng string hoặc node array; auto/copy chỉ là cấu hình yêu cầu.
- Snapshot có budget 4 giây, coalesce các lần refresh trên cùng player; host dùng correlation ID/deadline 6 giây. Command deadline 10 giây bao cả lock/write/response; dispose/disconnect kết thúc pending diagnostics. Worker xử lý snapshot độc lập để pause/play không phải chờ toàn bộ phép đo. Log một lần khi load hoàn thành và đo khi user mở/refresh, không thêm polling liên tục.
- Kiểm thử EXE: mở thủ công 270 ms, startup 15,24 giây; RAM khi chờ 11,44 MiB, chưa có library/cache I/O hoặc decoder. Ngắt process kiểm thử khi chờ không để lại dữ liệu dở. Diagnostics 30 assertions, core 22/22, native MP4/WebM/QoL/UX/transitions qua; build/publish 0 warning/0 error.
- Source/preview version 0.4.5; stable 0.4.4 và ZIP giữ nguyên. Build mặc định output development; package kiểm tra sourceVersion. 15/15a, 16–17, 17a.1–3, 18.1, 18a.1–2 và 18b.1–2 done theo scope. Còn Windows/thiết bị thật, cold boot, VRAM/soak 8/24 giờ và release gate 20. Chưa đóng gói release 0.4.5.

Bằng chứng/source/reproduce/giới hạn: reports/IMPLEMENTATION-06-STARTUP-DIAGNOSTICS.md, IMPLEMENTATION-07-HARDWARE-DECODE.md và IMPLEMENTATION-08-PLAYER-LIFECYCLE.md. Preview mới: artifacts/stage3-lifecycle-preview/TienDang.Wallpaper.exe; preview bước 15/15a giữ để đối chiếu lịch sử.
### Bước 15a — hardware decode (04/10/2026)

- Ma trận native 52/52 ca: H.264/HEVC MP4, VP9/AV1 WebM và MP4, 1080p/4K60, Original/15/30/60. Trên RTX3070/driver32.0.15.9186,48 ca8-bit4:2:0 đọc được d3d11va/d3d11va-copy. Bốn ca H.264High10 fallback CPU được nhận diện thật, không tick hardware pass.
- CPU/RAM/GPU/dedicated-shared memory lấy theo ownedPID; phase marker ngăn gắn nhầm telemetry cho player khác. Timeline/FPS, pending IPC và decoder exit qua; dữ liệu raw và scripts reproduction giữ trong tests/HardwareMatrix.
- H.2644K đo ba run: CPUmedian Original0,54%,15FPS4,29%,30FPS4,27%,60FPS4,27% tổng16cores. CPUcopy/filter có thể tăng chi phí dù vẫn decodeGPU; giữ Original mặc định, không quảng cáo capFPS là tiết kiệm CPU/điện. Có1outputdrop ở một sample cap60, không tuyên bố zero drops.
- Banner softwarefallback một lần mỗiitem, reset khi library/relink thay đổi; không tựstop/transcode hoặcSave. Guidance ENVI cho lighterH2648-bit1080p/ảnh tĩnh; diagnostics giải thích copybackend, settings khuyên thửOriginal nếu cap làmgiật.
- Build sạch;core22/22,diagnostics35assertions,UX ENVI/drop/sizing và nguồn12FPS qua. 15a done trong scopeGPU/profile đã đo; outputwindow720×450,samplingngắn,không HDR/VFR/AMD/Intel/đa màn hình/soak dài. Báo cáo IMPLEMENTATION-07-HARDWARE-DECODE.md. Kết quả controller/lifecycle tiếp nối ở mục bên dưới.
### Controller / IPC / Windows lifecycle — lượt 04/10/2026

- Requested/active/HasFrame commit theo loaded generation; thời gian riêng/LastItemId/pin không cập nhật sớm. Bộ lỗi/rỗng giữ frame và báo trạng thái; Apply cùng item sau transition lỗi tạo lại decoder. Controller 50 assertions.
- ACK/correlation cho command, 10s lock/write/response, ready12s, diagnostics6s, load-ready35s; cancellation, host epoch và one-fault-one-retry. Backoff1s/2s, dừng sau lần lỗi thứ ba; 30s ổn định mới reset. Private Windows Job Object cho worker/MPV, exit completion được chờ trước host/decoder mới.
- Snapshot-to-file với budget4s; bridge≤2.073.600pixel, ảnh≤8.294.400pixel; quit1s rồi kill/wait3s. Một native HWND video và một bridge được dùng lại; bitmap bỏ reference, semaphore giữ thứ tự cancel/latest; không GC.Collect mỗi lượt.
- Fix tăng named IPC handle trong mixed WPF hardware/external D3D11 trên máy QA: riêng worker WPF static-image composition dùng SoftwareOnly, MPV vẫn GPU/D3D11VA/copy. Main UI/preview không đổi. Thử tắt flip không hiệu quả và đã bỏ; không sửa NVIDIA/overlay của user.
- Final48 handoff4K: mọi owned PID thoát,47 exit-before-start, bridge1080p; warm-to-end RAMmedian -2,01MiB/handles -5. GPUcounter59mẫu/48PID; snapshot xác nhận hwdec-current d3d11va. Đây là test ngắn trong cửa sổ720×450, không phải soak hay desktop4K output.
- Ba SetWinEventHook OUTOFCONTEXT; fullscreen/maximize riêng ENVI, maximize mặc địnhfalse. Xét mọi visible covering window theo monitor, giữ manual/battery/lock/Remote policy. Native29mẫu event→worker→MPV ACK p95 16ms/max31ms; command→pauseobserved p95 80,91ms/max84,50ms. Unhook/owned subtree exit qua.
- Resolution/replug giữ requested/pin/remaining, retire old host; virtual unplug/replug và lock/display clock qua. Native watchdog reattach owned test parent qua; chưa restart Explorer máy user.
- UX/drop/sizing ENVI/maximize persistence + rollback, core22/22, IPC/diagnostics/12FPS/native transitions, fresh MP4/QoL và WebM import/thumbnail/preview qua. Sửa newline tên sidebar bị XML normalize/cắt chữ, title 0.4.5; publish và manual/startup EXE smoke qua. Timed presenter soak CLI --cleanup-soak --hours8|24 đã chuẩn bị, chưa chạy. Cold boot, sleep/lock/game/đa monitor thật,8/24h và release20 vẫn mở. Preview artifacts/stage3-lifecycle-preview; báo cáo IMPLEMENTATION-08-PLAYER-LIFECYCLE.md; stable0.4.4 không thay.
## 0.4.4 — 04/10/2026
Chặng 2 bước 09–14: thư viện lớn nhẹ hơn, giữ UI/import/playback đã có; schema Version 1 không đổi.

- **Filmstrip theo viewport.** Horizontal VirtualizingStackPanel recycling, pixel scroll, 0.5 viewport buffer. 1.000/10.000 mục chỉ 7 container lúc mở; chọn/cuộn/keyboard/resize/menu/multi-selection giữ đúng. Bằng chứng bước 09: IMPLEMENTATION-04-FILMSTRIP.md.
- **Thumbnail lazy-load và cache có budget.** Theo realized row + selected card, ưu tiên selection, hai producer; consumer-count dedup/cancellation, bỏ bitmap reference offscreen. Frozen bitmap LRU theo byte, UI budget 48 MiB dưới ceiling 64 MiB. Disk cache v2 key path/length/mtime/kích thước, publish atomic, sweep quota mục tiêu 128 MiB theo chu kỳ 30 giây. Không giữ Task đã hoàn thành mãi hoặc repopulate cache bằng job đã cancel.
- **Decode và I/O.** Long-side thumbnail 480px giữ aspect kể cả ảnh dọc; File.Exists/metadata/decode ở background. Video extraction một frame có timeout 15 giây, cancel/kill/wait đúng owned process và temp cleanup. Thumbnail hwdec=no khác playback hwdec=auto/auto-copy; không thay pipeline desktop/FPS.
- **Search.** Debounce 150 ms, explicit filter/sort/clear áp dụng ngay. Cùng sequence giữ ItemsSource/selection/scroll, purge card đã xóa; detail dùng cached FileAvailable. Không kiểm tra toàn thư viện trên đường layout.
- **Checkpoint không ghi trên UI.** Snapshot clone tất cả persisted fields trên UI, serialize/disk ở Task.Run, dirty flag/một checkpoint in flight. Shared StateStore write gate + Revision bỏ stale snapshot sau explicit commit. Failed checkpoint báo lỗi/retry; idle clean không ghi. Explicit mutation/final shutdown Save vẫn sync để giữ rollback; có thể đợi writer đang giữ gate.
- **Thu tray.** Hủy request/giải phóng thumbnail cache và image/video preview; show reload selection kể cả cùng path. Dispose cancel producer, bounded shutdown wait 2 giây; không GC.Collect mỗi lần đổi wallpaper.

Đo cùng fixture sau bước09: 1.000 distinct JPEG, initial private bytes từ 615,21 MiB xuống 105,38–106,36 MiB (ba run); cuối vòng cuộn 221,84–225,86 MiB; peak bitmap cache 47,96 MiB. Search 96 mẫu p95 apply/layout 25,14 ms, ký tự cuối→layout 181,63 ms; viewport ready 669–678 ms. 10.000 ảnh: initial 125,42 MiB, cuối vòng 228,28 MiB, search E2E p95 184,26 ms. RAM là MainWindow JPEG fixture 1280×720 ở cửa sổ 1280×850; chưa 4K/video/all-process/8–24h soak. Idle clock ghi 0 trên mẫu ngắn không có nghĩa zero CPU.

Validation: build/publish sạch; core 22/22; cache eviction/cancel/invalidation/disk/portrait/corrupt; save-failure 33 assertions gồm background checkpoint; native MP4/WebM/QoL/UX EN-VI/drop/direct URL/transitions/filmstrip qua; EXE portable smoke/recovery 6/6. ZIP verify từng entry/hash/tool/version bằng package-release.ps1. Raw first-pass không đạt E2E 251,3 ms được giữ; sau chỉnh cache/debounce đã qua gates. Report IMPLEMENTATION-05-LIBRARY-PERFORMANCE.md, handoff DEVELOPMENT-HANDOFF-0.4.4.md, package-results.json và source context đi kèm.

Live YouTube/FPS GPU không retest vì downloader/player unchanged; logs 0.4.3 là bằng chứng lịch sử. Chưa event hook/maximize (18a), chưa old-exit→new-start (17a), chưa hardware codec/fallback matrix (15a). Tiếp theo Chặng 3: bước 15 diagnostics trước, rồi 15a/16/17/17a/18/18a/19/20; không tick ba mục này từ transition test.
## 0.4.3 — 04/10/2026

Bản UX và FPS trên base đã duyệt; schema Version1, local/direct-URL/YouTube import giữ flow hiện tại.

- **Shuffle bắt đầu đúng selection.** Rotation.Start phát preferred hợp lệ trước và đưa N−1 mục còn lại vào bag; hết vòng mới lặp, tránh lặp liền nhau qua boundary. Sequential tiếp từ selection. Hai nút start truyền cùng ID; đang phát đúng item giữ host/frame và reset clock. Lịch theo giờ vẫn ưu tiên bộ của lịch; selection bên ngoài bộ được resolve không chen vào.
- **Kéo thả.** PreviewDragOver/PreviewDrop nhận FileDrop tại window, zone trong library, whitelist định dạng, Copy cursor. Gọi Import hiện tại để giữ scan/folder/dedup/copy/transaction. Sau commit chọn item mới để preview; không auto desktop. Unsupported file không được offer Copy. Import rỗng không Save không cần thiết.
- **EN/VI.** Observable language + XAML Loc binding, 371 English keys và VI overrides. Dịch nguyên câu/template số, không word-by-word. Refresh dialog/tray/status/cards/display/file picker; tên user/path không dịch, All collection nhận bằng ID. Language save failure rollback, đổi sau commit, không restart app/decoder preview.
- **Sizing.** Kín màn hình / Giữ trọn ảnh / Kéo vừa màn hình, có mô tả crop/bars/distortion. Ba card dùng cùng DrawingImage dọc và Stretch WPF thật để hình khác nhau có ý nghĩa. Save/Cancel luôn ngoài vùng scroll.
- **FPS.** AppSettings.FrameRateLimit0/15/30/60 mặc định0, Languagevi/en defaultvi, normalize whitelist. Preferences snapshot/rollback thêm cả hai. PlayerMessage mang FPS/Language, engine/presenter/worker/wrapper cập nhật active và candidate latest options. Preview giữ Original. Lavfi fps dùng min(source_fps,cap); nguồn FPS thấp không tăng frame/tốc độ. Runtime disable bỏ vf và khôi phục hwdecauto.
- **Giới hạn hiệu năng.** Cap dùng hwdecauto-copy vì CPU filter cần đọc frame. Đo outputFPS thật 60→15/30/60 và12→12, timeline và D3D11 qua. Chưa kết luận giảm decode/CPU/VRAM/power với mọi codec/4K; Original vẫn mặc định.
- **Cửa sổ nhỏ.** Hàng riêng cho video-end và bộ sưu tập, EN đọc được tại1040×690. Card/bitmap preview được giữ khi đổi ngôn ngữ.
- **Startup.** Khi normal packaged launch với data-dir mặc định, entry đã enabled refresh về EXE đang chạy; không tự bật, smoke/dotnet/custom không đổi startup cá nhân. Settings save refresh target kể cả bool không thay đổi. Không dừng phiên hiện tại trong release QA.

Validation: core 21/21; nativeUX34 assertions, save rollback 31, nativeQoL/WebM, URL English (HTML/invalid/cancel/unknown size), transitions với cap, FPS headless/GPU và portable smoke6/6. Source map/reproduce và hạn chế: reports/IMPLEMENTATION-03-UX-FPS.md và DEVELOPMENT-HANDOFF-0.4.3.md. Ngôn ngữ/error diagnostics của Windows/MPV vẫn theo nguồn khi không phải app message.

Next: 09filmstripviewport →10thumbnail/cache →11search →12save nền →13benchmark →14release0.4.4; lifecycle/profiles đo resource ở0.4.5. Không tick tối ưu thư viện chỉ vì thêm settingFPS.
## 0.4.2 — 03/10/2026

Bản vá dữ liệu và xử lý Save failure trên nền 0.4.1. WPF/.NET 10, portable self-contained Windows x64. Giữ UI đã duyệt, local import, direct URL và YouTube. Schema library vẫn Version 1; SourceUrl giữ tương thích, không cần migration/import lại.

### Lỗi đã sửa và hành vi

**DATA-01 — recovery bị bỏ qua khi collection null.** JSON có Items/Playlists/Settings/Monitors/Schedules hoặc record lồng nhau null từng gây lỗi LINQ/Normalize. LibraryState.ValidateStructure giờ chạy trước Normalize và chuyển lỗi cấu trúc thành JsonException. StateStore.Load phục hồi backup, giữ primary hỏng dưới tên library-damaged-<timestamp>-<GUID>.json và báo kết quả thật. Primary mất nhưng backup còn cũng phục hồi được. Nếu cả hai không hợp lệ, mở thư viện trống có cảnh báo; đây không phải salvage record từng phần.

**DATA-02 — backup tốt bị primary hỏng ghi đè sau recovery.** Save lấy snapshot byte của primary rồi parse/validate trước khi dùng làm backup. Primary mất/hỏng không thay .bak hiện có. Vì vậy primary hỏng → recovery → Save → primary hỏng lần nữa vẫn phục hồi được. Kiểm tra áp dụng cả trường hợp primary bị thay ngoài app sau Load.

**DATA-03 — thao tác báo thành công dù Save trả false.** Favorite đơn/nhiều mục, tạo/đổi/xóa bộ, thêm vào bộ, thời gian riêng, reorder, remove/undo, relink, lịch và preferences biết kết quả commit. Save thất bại hoàn tác phần metadata vừa sửa, giữ thông báo lỗi; player/view và thông báo thành công chỉ cập nhật sau commit. Xóa bộ thất bại khôi phục vị trí bộ, profile và lịch liên quan. Undo thất bại giữ action để thử lại.

**Import local và URL.** Quét file/thư mục, dedup Path/OriginalPath, CopyOnImport và phân loại media giữ nguyên. Helper ImportLocalFileAsync chuẩn bị item; caller mới đăng ký vào state ngay trước Save. Copy async không để record chưa commit lộ ra cho Save khác. Save lỗi hoàn tác record/membership/SourceUrl thuộc thao tác. Media tải/copy được giữ, không tự xóa; user có thể thêm lại. URL tải trong media không copy lần hai; đích ngoài vẫn tôn trọng CopyOnImport.

**Draft setting.** CaptureSettings chỉ validate/capture; nhãn Đã lưu chuyển sau Save thành công. Save lỗi khôi phục interval/shuffle/video-end trong state, giữ giá trị draft trên UI, nhãn Chưa lưu và nút retry. Preferences/lịch rollback riêng. Startup registration chỉ đổi sau lưu metadata. WallpaperEngine.SettingsChanged lưu trước áp dụng; ApplySavedSettings cập nhật order/clock/options cho state đã commit.

### Đường ghi dữ liệu

1. Validate cấu trúc và Version 1; serialize UTF-8 trước khi chạm file đã commit.
2. Tạo primary temp có GUID cùng thư mục, FileMode.CreateNew/FileShare.None, Flush(true).
3. Đọc và validate exact bytes của primary. Nếu hợp lệ, ghi/flush backup temp riêng rồi File.Move(overwrite: true) vào .bak.
4. Publish primary temp bằng File.Move(overwrite: true).
5. finally dọn hai temp do lần Save sở hữu. Lỗi cleanup ghi log và không che exception gốc.

Hai file primary/backup không tạo thành một transaction filesystem duy nhất. Test lock/serialize/partial-temp không chứng minh độ bền qua mất điện/full-disk. File hỏng được giữ best-effort; nếu không copy được, notice nói rõ. Lỗi quyền đọc dữ liệu được phép nổi lên thay vì âm thầm ghi đè thư viện không truy cập được.

### Phạm vi source

| File | Trách nhiệm thay đổi |
|---|---|
| src/TienDang.Core/Models.cs | ValidateStructure trước Normalize |
| src/TienDang.Core/StateStore.cs | Load recovery, damaged preservation, validated backup, durable temp và cleanup |
| src/TienDang.App/MainWindow.Persistence.cs | CommitMutation + rollback cụ thể, rotation/preferences/schedules |
| src/TienDang.App/MainWindow.xaml.cs | Local staging, các event gọi mutation biết kết quả Save |
| src/TienDang.App/MainWindow.Qol.cs | Favorite/relink/removal/undo chỉ notify sau commit |
| src/TienDang.App/MainWindow.Reference.cs | URL caller đăng ký staged item; rotation dùng save gate |
| src/TienDang.App/WallpaperEngine.cs | Tách Save khỏi áp dụng setting đã commit |
| src/TienDang.App/WallpaperPreview.cs | HasVideoFrame ghi nhận playback-restart với generation guard, reset khi release |
| tests/TienDang.Tests/PersistenceChecks.cs | Null schema, backup lặp, file lock/serialize/partial-temp |
| tests/TienDang.UiTests/SaveFailureChecks.cs | Regression native rollback/retry |
| tests/TienDang.UiTests/UrlChecks.cs, YouTubeUiChecks.cs | Capture đợi decoded frame, tránh nhầm file-loaded với frame-ready |
| build.ps1, csproj, MainWindow.xaml, tài liệu | Version/release path và tài liệu bàn giao |

### Validation

- Code cũ: ba nhóm persistence mới thất bại; sau sửa core 18/18 nhóm qua.
- Native Save failure: 31 kiểm tra rollback/retry qua, dùng primary bị khóa trên Windows.
- Native UI/QoL và WebM local: preview/thumbnail, file/folder import, dedup, copy, undo/relink, draft và layout đã qua.
- URL loopback: MP4/WebM/JPEG, progress/unknown-total, cancel, HTML/corrupt media và persistent registration qua.
- YouTube live sample jNQXAC9IVRw: metadata → download/merge/remux → chosen folder + CopyOnImport → selected playlist → JSON → decoded local playback → duplicate short URL qua ngày release.
- Kết quả EXE smoke/recovery, package hash/size/toolset được ghi riêng trong RELEASE-0.4.2.md và RELEASE-MANIFEST.json.

### Chưa giải quyết

PERF-01/02/03/04: filmstrip toàn bộ mục, thumbnail eager không budget, search đồng bộ và full JSON Save trên UI. Validate backup thêm parse/I/O; chưa có benchmark Save mới để tuyên bố cải thiện tốc độ. Số đo RAM/search từ audit 0.4.1 là baseline, không phải số đo 0.4.2.

PLAY-01/02, UX-01: requested/active, IPC deadline, DPI nhỏ, nhiều màn hình/lifecycle/soak còn gate riêng. DL-01/02, OBS-01: crash staging cleanup, cập nhật toolset có verify/rollback, diagnostics chưa triển khai. Giữ old/new handoff hiện tại; chưa thêm crossfade, profile, queue hoặc đổi UI.

### Cập nhật và rollback

Thoát app cũ, giải nén cả bộ 0.4.2 vào thư mục riêng rồi mở EXE mới. Data directory mặc định không đổi. Với --data-dir, tiếp tục dùng cùng đường dẫn. Không xóa media khi nâng cấp. Sao lưu thư mục dữ liệu khi app đã thoát nếu cần một snapshot thư viện.

Rollback bằng EXE 0.4.1 trong thư mục riêng và cùng data directory; schema tương thích, nhưng bản cũ vẫn chứa lỗi recovery/Save đã sửa. Rollback executable không tự hoàn tác thao tác library đã lưu bởi bản mới. Startup shortcut cần tắt/bật lại nếu chuyển đường dẫn app.

## 0.4.1 — 03/10/2026

- YouTube single-video qua yt-dlp 2026.08.19 + Deno 2.9.7 + ffmpeg; công cụ đóng gói/pin/hash.
- ProcessStartInfo.ArgumentList, UseShellExecute=false; canonical URL một argument sau --; config/plugins yt-dlp ngoài app tắt.
- Staging trên disk, byte progress/merge phase, cancel process tree, validate media rồi publish MP4/import.
- SourceUrl additive; shared helper local, CopyOnImport và selected playlist; test live AV1 + AAC qua.
- Public single videos; không tự đọc cookie, không download playlist hoặc livestream đang chạy.

## 0.4.0

- Native WPF theo reference một bản đã duyệt: mascot sidebar, real thumbnail list, preview, filmstrip và rotation bar.
- Import từ máy/thư mục/drop, direct HTTP/HTTPS URL hoặc clipboard trên click.
- Streaming disk, Content-Type/error/truncation validation, progress/cancel, chống ghi đè tên và validate media trước import.
- Preview generation guard chống kết quả decoder cũ làm lỗi lựa chọn mới.

Bằng chứng lịch sử chi tiết trong VALIDATION.md; audit/roadmap/checklist trong reports.