# SCADA_TEMPLATE — Architecture V1

## 0. Vai trò của tài liệu

Đây là specification nền cho một SCADA reusable bằng WPF.

Các yêu cầu trong tài liệu này được xem là baseline V1.

Không tự ý thay đổi kiến trúc nền nếu không có lý do kỹ thuật rõ ràng.

Nếu gặp điểm chưa được định nghĩa:

* ưu tiên giải pháp đơn giản;
* dễ bảo trì;
* dễ debug;
* phù hợp môi trường công nghiệp;
* không khóa hệ thống vào một vendor;
* giữ khả năng mở rộng trong tương lai;
* không over-engineer.

Không tự ý triển khai các tính năng tương lai chỉ vì kiến trúc có chừa đường cho chúng.

---

# 1. Mục tiêu quan trọng nhất

Đây không phải SCADA cho một máy cụ thể.

Đây là một thư mục mẫu:

`SCADA_TEMPLATE`

Workflow sử dụng phải là:

```text
SCADA_TEMPLATE
      ↓ Copy nguyên folder
PROJECT_NEW
      ↓
Mở Scada.sln
      ↓
Khai báo PLC
      ↓
Khai báo Tag
      ↓
Thiết kế UI máy bằng WPF
      ↓
Build / Run
```

Một dự án mới không cần tạo lại framework.

Toàn bộ cấu trúc phải được copy theo.

Không yêu cầu cài một framework SCADA nội bộ riêng.

Không được phụ thuộc source nằm ngoài thư mục dự án.

Không dùng absolute path kiểu:

```text
C:\SomeLibrary\
D:\SCADA\Common\
```

Mọi ProjectReference phải nằm trong solution và dùng relative path.

NuGet package được phép sử dụng.

---

# 2. Cấu trúc Solution chính thức V1

```text
SCADA_TEMPLATE/
│
├── Scada.sln
│
├── Scada.Core/
│
├── Scada.Runtime/
│
├── Scada.Drivers/
│
├── Scada.Infrastructure/
│
├── Scada.App/
│
├── tests/
│
├── docs/
│
├── scripts/
│
└── Deployment/
```

Chỉ có 5 project sản phẩm chính:

```text
Scada.Core
Scada.Runtime
Scada.Drivers
Scada.Infrastructure
Scada.App
```

Không chia nhỏ thành quá nhiều project ở V1.

---

# 3. Dependency architecture

Dependency phải một chiều.

Không circular reference.

Nguyên tắc:

```text
Scada.Core
   ▲
   ├──────── Scada.Drivers
   ├──────── Scada.Runtime
   ├──────── Scada.Infrastructure
   └──────── Scada.App
```

Có thể điều chỉnh reference cụ thể nếu cần để giữ compile-time architecture sạch, nhưng các quy tắc sau là bắt buộc:

```text
Scada.Core
    không phụ thuộc WPF

Scada.Runtime
    không phụ thuộc Scada.App/WPF

Scada.Drivers
    không phụ thuộc UI

Scada.Infrastructure
    không phụ thuộc UI

Scada.App
    là composition/UI layer
```

Không cho UI trở thành dependency của Runtime.

---

# 4. Scada.Core

`Scada.Core` chứa domain model và abstraction cơ bản.

Ví dụ:

```text
Scada.Core/
├── Tags/
├── Devices/
├── Drivers/
├── Alarms/
├── Historian/
├── MQTT/
├── Projects/
└── Common/
```

Tối thiểu chuẩn bị các concept:

```text
TagDefinition
TagValue
TagQuality
TagDataType

DeviceDefinition
DeviceState
DeviceConnectionState

ScanGroupDefinition

AlarmDefinition
AlarmLifecycleState

RuntimeOptions
ProjectDefinition
```

Driver abstraction:

```text
IPlcDriver
```

Core không chứa implementation Siemens/Mitsubishi/InfluxDB/SQLite/WPF.

---

# 5. Scada.Drivers

Mục đích:

```text
PLC / protocol communication
```

Cấu trúc dự kiến:

```text
Scada.Drivers/
├── Simulator/
├── Siemens/
├── Mitsubishi/
├── ModbusTcp/
└── OpcUa/
```

Không cần implement tất cả driver ngay trong milestone đầu.

Simulator là driver bắt buộc đầu tiên.

Các driver phải đi qua abstraction chung.

Runtime không được hard-code:

```text
if Siemens...
if Mitsubishi...
```

ở khắp nơi.

---

# 6. Scale mục tiêu

Kiến trúc phải được thiết kế cho khoảng:

```text
nhiều loại PLC
hàng chục PLC
~10.000 tags
```

Một Runtime ban đầu phải được thiết kế để có khả năng xử lý:

```text
~50 PLC
~10.000 tags
```

nhưng hiệu năng phải được xác minh bằng benchmark/stress test, không được chỉ khẳng định bằng lý thuyết.

---

# 7. Quy tắc hiệu năng bắt buộc

Có 4 cơ chế nền bắt buộc:

```text
1. Batch Read
2. Scan Groups
3. Central TagCache
4. Subscription-based UI updates
```

---

# 8. Batch Read

Không được mặc định đọc từng tag bằng từng request PLC.

Sai:

```text
Read D100
Read D101
Read D102
...
```

Ưu tiên:

```text
Batch Read D100 → D199
```

Tương tự với Siemens:

```text
Read DB block / byte range
```

rồi decode trong memory.

Driver architecture phải cho phép mỗi protocol tối ưu read planning theo đặc tính riêng.

---

# 9. Scan Groups

Không scan tất cả 10.000 tag cùng tốc độ.

Hỗ trợ các nhóm kiểu:

```text
Fast       ~100 ms
Normal     ~500 ms
Slow       ~1000 ms
VerySlow   ~5000 ms
```

Con số cụ thể phải configurable.

Tag chỉ chọn ScanGroup.

Ví dụ:

```text
Motor.Run          Fast
Motor.Fault        Fast
Tank.Level         Normal
Temperature        Normal
ProductionCount    Slow
RuntimeHours       VerySlow
```

---

# 10. Device isolation

Không poll 50 PLC tuần tự trong một vòng duy nhất.

Mỗi Device phải được quản lý độc lập/asynchronous.

Ví dụ:

```text
PLC01 Worker ───┐
PLC02 Worker ───┤
PLC03 Worker ───┤
...             ├──→ TagCache
PLC50 Worker ───┘
```

Không nhất thiết tạo một physical thread cho mỗi PLC.

Ưu tiên async I/O phù hợp với .NET.

Một PLC timeout/disconnect không được block polling của các PLC còn lại.

Mỗi device cần có trạng thái riêng:

```text
Connected
Connecting
Disconnected
Faulted
```

và cơ chế:

```text
Timeout
Retry
Reconnect
Cancellation
Statistics
```

---

# 11. TagCache

`TagCache` là nguồn dữ liệu runtime trung tâm.

Luồng bắt buộc:

```text
PLC
 ↓
Driver
 ↓
Polling
 ↓
TagCache
 ├── WPF
 ├── Alarm
 ├── Historian
 └── MQTT
```

Không được:

```text
WPF → PLC
Historian → PLC
Alarm → PLC
MQTT → PLC
```

để đọc lại cùng dữ liệu.

Một lần đọc PLC phải phục vụ toàn hệ thống.

---

# 12. UI subscription

Có 10.000 tag không có nghĩa WPF nhận notification của toàn bộ 10.000 tag liên tục.

Mỗi View chỉ subscribe các tag mà View đó sử dụng.

Ví dụ:

```text
TagCache: 10.000 tags

Process A → 80 tags
Process B → 150 tags
Process C → 60 tags
```

Khi View không còn active phải cho phép unsubscribe.

Không flood WPF Dispatcher bằng update không cần thiết.

---

# 13. Runtime V1

V1 chỉ có:

```text
1 project
1 runtime
n PLC
~10.000 tags
```

Không implement distributed multi-runtime ở V1.

Runtime chỉ cần có:

```text
RuntimeId = "Runtime01"
```

và đặc biệt:

```text
Scada.Runtime không phụ thuộc WPF.
```

Mục tiêu là tương lai có thể tách:

```text
Runtime PX1
Runtime PX2
Runtime PX3
```

mà không phải viết lại engine.

Nhưng KHÔNG implement:

```text
gRPC
SignalR
Runtime discovery
distributed synchronization
multi-runtime networking
```

ở V1.

---

# 14. Scada.Runtime

Cấu trúc dự kiến:

```text
Scada.Runtime/
├── Engine/
├── Devices/
├── Polling/
├── Tags/
├── Commands/
├── Alarms/
└── Historian/
```

Các component chính:

```text
ScadaRuntime
DeviceManager
ScanGroupManager
PollingWorker
TagEngine
TagCache
TagSubscription
TagWriteService
AlarmRuntimeService
HistorianQueue
```

Không cần tất cả tính năng nâng cao hoàn chỉnh ngay milestone đầu.

## 14.1 Alarm System V1

Alarm System là **PLC-read-only**: ACK được phép thay đổi trạng thái Alarm trong SCADA và event journal, nhưng Alarm không đọc lại PLC và không ghi PLC, Driver, TagCache hoặc MQTT.

Luồng bắt buộc:

```text
PLC / Simulator
 ↓
Driver + Polling
 ↓
TagCache
 ↓
AlarmRuntimeService
 ├── Alarm runtime snapshots → Operation / Monitoring
 └── bounded persistence coordinator → IAlarmEventStore → SQLite
```

Runtime subscribe đúng một lần cho mỗi logical TagId khác nhau, sau đó fan-out tới các Alarm definition liên quan. Phải subscribe-before-seed. Không được tạo timer hoặc Task riêng cho từng Alarm/tag; toàn bộ activation delay dùng một monotonic deadline coordinator chung.

Implementation boundary: Runtime giữ một index case-insensitive từ logical `TagId` tới các Alarm definition liên quan và thứ tự Alarm đã precompute. Mỗi callback chỉ fan-out tới nhóm khớp; nếu lifecycle/quality/availability/diagnostic không thay đổi thì callback kết thúc trước khi materialize snapshot, so sánh toàn bộ Alarm list, publish hoặc fan-out subscriber. Raw source sequence/timestamp vẫn được giữ trong mutable runtime state. `AlarmSnapshot.LastSourceSequence` và `LastSourceTimestampUtc` phản ánh metadata của public snapshot được materialize gần nhất, nên có thể cũ hơn source metadata mới nhất khi raw-only update bị suppress; consumer cần từng raw source update phải dùng central TagCache contract. `StaleTagUpdates` và `SubscriberExceptions` là diagnostic snapshot fields và được đưa vào meaningful-change comparison khi snapshot mới được materialize. Snapshot thay đổi có ý nghĩa được App-layer Operation/Monitoring giao theo latest-state coalescing, tối đa một Dispatcher item cho mỗi active generation; stale callback sau deactivate/dispose phải bị bỏ qua.

Rule V1 của Alarm gồm:

```text
DigitalEquals
High
HighHigh
Low
LowLow
```

Digital rule chỉ dùng Boolean. Numeric rule dùng Int32, Int64 hoặc Double. High return khi giá trị `<= threshold - deadband`; Low return khi giá trị `>= threshold + deadband`.

State machine chuẩn:

| Current state | Trigger | Next state |
|---|---|---|
| Normal | Good value thỏa rule, delay chưa đủ | PendingActivation nội bộ |
| PendingActivation | monotonic delay đủ và Good value vẫn thỏa rule | ActiveUnacknowledged hoặc ActiveAcknowledged |
| PendingActivation | rule return hoặc quality unavailable trước deadline | Normal |
| ActiveUnacknowledged | ACK đúng InstanceId | ActiveAcknowledged |
| ActiveUnacknowledged | Good value return | ReturnedUnacknowledged |
| ActiveAcknowledged | Good value return | Normal/closed |
| ReturnedUnacknowledged | ACK đúng InstanceId | Normal/closed |
| ReturnedUnacknowledged | condition active lại | ActiveUnacknowledged, giữ cùng InstanceId |

ACK phải target exact `InstanceId`; stale ACK không mutate state và duplicate ACK phải idempotent. ACK-all snapshot danh sách InstanceId hợp lệ rồi dùng cùng per-instance path. M11 không implement authentication/authorization hoặc Alarm-to-PLC ACK.

Chỉ `TagQuality.Good` được activate hoặc return Alarm. Bad, Uncertain, Disconnected và NotConfigured giữ nguyên lifecycle state và chỉ cập nhật evaluation availability/quality trong runtime snapshot và diagnostics. Không journal từng quality flap và không tự động tạo communication alarm trong M11.

Clock contract bắt buộc tách riêng:

```text
observable transition / ACK / journal timestamp
→ TimeProvider.GetUtcNow()

activation deadline / elapsed time
→ TimeProvider.GetTimestamp() + monotonic elapsed APIs
```

Wall-clock jump tới trước hoặc lùi không được làm Alarm activate sớm, trễ hoặc restart deadline.

Project schema M11 là v6. Migration v5 → v6 tạo `AlarmOptions.Enabled = false`, vì project hiện có không được tự động phát sinh Alarm runtime behavior. Migration chỉ diễn ra in-memory; explicit Save mới rewrite project document.

Alarm SQLite mặc định:

```text
Data/alarms.db
```

Path này resolve tương đối từ canonical `ProjectPath.DirectoryPath`. Khi Alarm persistence enabled, ProjectPath là bắt buộc; path rỗng, rooted/absolute hoặc traversal ra ngoài project directory phải bị reject. Không source-tree discovery và không fallback sang `AppContext.BaseDirectory`/output directory.

AlarmEvents persistence schema hiện tại là v2: `SourceQuality` là nullable để phân biệt quality đã biết với legacy event không có quality. Khởi tạo database fresh tạo v2; database v1 được nâng cấp bằng migration explicit và các row cũ giữ `NULL`, không được fabricate quality.

Persisted Alarm state chỉ là authority khi checkpoint trước được đánh dấu trusted sau một session gap-free, queue drain thành công và final open-instance checkpoint được commit atomically.

Khi Alarm persistence enabled, durable recovery-untrusted marker là **hard startup precondition**:

1. Trước khi Alarm evaluation hoặc subscription active, session mới phải atomically và durably ghi `RecoveryTrusted = false` cùng continuity/session metadata.
2. Chỉ sau khi commit marker thành công mới được acquire TagCache subscription, seed/reconcile TagCache, bắt đầu activation deadline và tạo/mutate live Alarm lifecycle state.
3. Nếu marker không commit được, Alarm evaluation không được start; Alarm không được subscribe TagCache, không được tạo/mutate live Alarm state và tuyệt đối không được fallback sang memory-only Alarm session.
4. Alarm subsystem phải vào explicit Degraded/Faulted startup state và expose persistence startup failure qua diagnostics/UI. PLC polling và các subsystem SCADA khác vẫn được phép tiếp tục.
5. Không được fallback in-memory theo cách khiến trusted checkpoint cũ trong SQLite tiếp tục trông như authority ở process restart kế tiếp.

Khi clean shutdown, `RecoveryTrusted` chỉ được set true nếu session không có queue gap/drop/rejection/abandonment/write loss, final queue drain thành công, complete open-instance checkpoint tồn tại, và checkpoint cùng continuity/session metadata được commit atomically. Crash, queue drop, rejected persistence item, abandoned write, unrecoverable write failure hoặc drain timeout vĩnh viễn loại session đó khỏi trusted recovery.

Queue drop/reject, abandonment, write failure, crash hoặc drain timeout làm session không đủ điều kiện trusted recovery. Open instance chỉ restore khi checkpoint trusted và current Alarm definition có material fingerprint tương thích, bao gồm TagId, RuleType, expected value/threshold, deadband, delay, AckRequired và severity. Definition bị xóa, disable hoặc thay đổi material phải thành retired/orphaned history, không merge vào live state và không fabricate Return/Closed như dữ liệu PLC.

Nếu trusted recovered instance có current Good seed thì reconcile theo state machine. Nếu current quality unavailable thì giữ trusted lifecycle state và đánh dấu evaluation unavailable, không tự clear. Nếu recovery untrusted thì không inject persisted instance vào live state; reevaluate từ current Good TagCache seed và hiển thị recovery-untrusted diagnostics.

## 14.2 M11 verification contract

M11 phải có deterministic test contract riêng. Không dùng sleep để chứng minh concurrency hoặc timing.

### Clock và ActivationDelay

- Dùng controllable/fake `TimeProvider`.
- ActivationDelay dùng `TimeProvider.GetTimestamp()` và monotonic elapsed APIs.
- `GetUtcNow()` chỉ tạo observable transition/ACK/recovery/journal timestamp.
- Wall clock nhảy tới trước khi monotonic elapsed nhỏ hơn delay: không activate.
- Wall clock nhảy lùi khi monotonic elapsed nhỏ hơn delay: không activate.
- Monotonic elapsed nhỏ hơn delay: không activate.
- Monotonic elapsed tới deadline: activate đúng một lần.

### State machine và ACK

- Cover toàn bộ transition của `AlarmLifecycleState`, bao gồm `ReturnedUnacknowledged` và same-instance reactivation.
- ACK phải target exact `InstanceId`; stale ACK không mutate instance mới hơn và duplicate ACK phải idempotent.
- ACK-all phải snapshot eligible InstanceId rồi gọi cùng per-instance ACK path.
- Race giữa ACK và current-value transition phải deterministic và không tạo duplicate/missing transition.

### TagCache và quality

- Runtime chỉ có một subscription cho mỗi distinct valid TagId và phải subscribe-before-seed.
- Alarm-created PLC reads phải bằng 0.
- Stale/duplicate `TagValue.Sequence` không được evaluate lại state.
- Bad, Uncertain, Disconnected và NotConfigured không được false activate, return hoặc clear.
- Quality unavailable phải giữ lifecycle state ở nơi state machine yêu cầu.

### Recovery và persistence

- Trusted compatible checkpoint restore đúng state và ACK.
- Untrusted checkpoint không được inject thành authoritative live state.
- Durable startup recovery-untrusted marker failure phải fail/degrade closed: không subscription, không evaluation, không deadline, không live-state mutation và không memory-only fallback.
- Queue drop/rejection, abandoned write, unrecoverable write failure hoặc drain timeout phải ngăn trusted checkpoint.
- Crash phải để startup tiếp theo ở recovery-untrusted.
- Cover Good-seed reconciliation và unavailable-quality reconciliation.
- Cover definition bị delete, disable và thay đổi từng material field: RuleType, threshold, expected value, deadband, ActivationDelay, AcknowledgementRequired, TagId và severity.
- Display-only fields phải giữ recovery compatibility.
- Orphaned/retired history vẫn query được; configuration reconciliation không được fabricate PLC `Returned`/`Closed` transition.
- Cover corrupt/newer SQLite schema, missing `ProjectPath`, rooted/absolute path và traversal ra ngoài canonical project directory.

### Lifecycle và isolation

- Alarm-owned subscriptions sau shutdown phải bằng 0.
- Subscriber exception phải được isolate.
- Faulty hoặc non-cooperative persistence store không được block PLC polling.
- Shutdown phải bounded và observe/cancel remaining work theo contract.

### M11 bounded scale sanity

M11 chạy một Alarm-specific bounded sanity với khoảng 10.000 project tags, representative Alarm definitions và deterministic transition burst để phát hiện:

- timer/Task theo từng Alarm;
- subscription theo từng Alarm thay vì theo distinct TagId;
- queue growth không bounded;
- snapshot publication rõ ràng không bounded;
- lock/contention regression nghiêm trọng.

Sanity này không thay thế, redefine hoặc yêu cầu chạy lại M10 qualification. SHA `402ee9d46f41489fee8912bbed57dc1388550658` tiếp tục là authoritative M10 benchmark không thay đổi.

---

# 15. Historian không được block PLC

Luồng:

```text
PLC
 ↓
TagCache
 ↓
HistorianQueue
 ↓
Background Writer
 ↓
SQLite / InfluxDB
```

Không:

```text
Read PLC
 ↓
await database write
 ↓
Read PLC tiếp
```

Database lỗi không được khiến PLC polling/HMI dừng.

---

# 16. Scada.Infrastructure

Chứa implementation liên quan đến:

```text
Configuration
Persistence
SQLite
InfluxDB
MQTT
Logging
Local buffering
```

Cấu trúc dự kiến:

```text
Scada.Infrastructure/
├── Configuration/
├── Historian/
│   ├── SQLite/
│   └── InfluxDB/
├── MQTT/
├── Logging/
└── Persistence/
```

---

# 17. Tag Manager — yêu cầu trọng tâm

Người triển khai dự án không nên phải chỉnh `tags.json` thủ công trong workflow bình thường.

Phải có Tag Manager trực quan bằng WPF.

Tag Manager cần hỗ trợ:

```text
Add
Delete
Edit
Duplicate

Search
Filter
Sort

Multi-select
Bulk edit

Copy/Paste nhiều dòng

Import/Export
ít nhất CSV ở V1 thích hợp

DataGrid virtualization
```

Mục tiêu quy mô:

```text
~10.000 tags
```

UI phải vẫn usable.

---

# 18. Tag Manager layout

Dùng pattern:

```text
Toolbar
Search / Filter
DataGrid
Selected Tag Detail
Advanced section
```

Không nhồi tất cả property thành hàng chục cột.

Các cột mặc định nên gọn:

```text
Enabled
Tag Name
Device
Address
Data Type
Scan Group
History
History Profile
MQTT Publish
MQTT Profile
Quality
```

Các property khác có thể qua:

```text
Detail Panel
Column Chooser
Advanced
```

---

# 19. Tag Definition

Tag cần chuẩn bị các nhóm thông tin:

```text
Identification
PLC
Acquisition
Engineering
Historian
MQTT
```

Ví dụ:

```text
Name
Description
Enabled

DeviceId
Address
DataType
AccessMode

ScanGroup

Scale
Offset
Min
Max
Unit

HistoryEnabled
HistoryProfile

MqttPublishEnabled
MqttProfile
```

Không bắt buộc người dùng nhập mọi field.

Default phải hợp lý.

---

# 20. Device selection

Device trong Tag Manager phải là dropdown/list lấy từ Device Manager.

Không để người dùng nhập DeviceId tự do nếu có thể tránh.

Tương lai có thể có Address Browser riêng cho từng driver.

Không bắt buộc triển khai Address Browser đầy đủ ngay V1.

---

# 21. Online Tag Monitor

Phải có màn hình để kiểm tra:

```text
Tag
Value
Quality
Timestamp
Device
```

Workflow:

```text
PLC
 ↓
Tag
 ↓
Online Monitor
```

Người triển khai phải kiểm tra tag chạy đúng trước khi thiết kế UI máy.

---

# 22. Historian UX

Historian phải:

```text
đơn giản ở bề mặt
đầy đủ khi cần Advanced
```

Trong Tag Manager, người dùng chủ yếu làm:

```text
History ✓
History Profile = Digital / Analog / Fast Analog
```

Không bắt người dùng bình thường phải hiểu:

```text
deadband
minimum interval
maximum interval
batch writer
```

---

# 23. History Profiles

Tối thiểu concept:

```text
Digital
Analog
Fast Analog
Custom
```

Ví dụ:

```text
Digital
→ lưu OnChange

Analog
→ optimized change/periodic behavior

Fast Analog
→ profile nhanh hơn

Custom
→ Advanced settings
```

Exact default values phải nằm trong configuration/profile, không hard-code rải rác.

---

# 24. History Advanced

Advanced có thể hỗ trợ:

```text
OnChange
Periodic
OnChange + Periodic

Deadband
Minimum interval
Maximum interval
```

Nhưng phải collapse mặc định.

---

# 25. Historian storage

Cho phép chọn:

```text
SQLite
InfluxDB
```

SQLite:

```text
dự án nhỏ
standalone
local database
```

InfluxDB:

```text
dự án lớn
nhiều historical tags
long-term time-series
```

Không ép mọi dự án dùng InfluxDB.

---

# 26. Historian global settings

Settings → History:

```text
Enabled
Storage provider
Retention
Connection settings
Buffering
Test Connection
Status
```

Không cấu hình server database lặp lại ở từng tag.

---

# 27. Historian buffering

Nếu external historian tạm mất:

```text
PLC        vẫn chạy
HMI        vẫn chạy
Alarm      vẫn chạy
History    buffering
```

Thiết kế có local buffer/queue phù hợp.

Khi historian trở lại:

```text
buffer
 ↓
background sync
 ↓
historian
```

Không làm Runtime freeze.

---

# 28. MQTT mục đích

MQTT là cầu nối để tương lai xây:

```text
Web Dashboard
Mobile
MES
other systems
```

Luồng:

```text
PLC
 ↓
TagCache
 ↓
MQTT Publisher
 ↓
MQTT Broker
 ↓
Web
```

MQTT không đọc lại PLC.

---

# 29. MQTT trong Tag Manager

Người triển khai chủ yếu thấy:

```text
MQTT Publish ✓
MQTT Profile = Web Digital / Web Analog
```

Hỗ trợ:

```text
Bulk Enable/Disable
Auto Topic
Profile
Advanced settings khi cần
```

---

# 30. MQTT topic

Không bắt nhập thủ công 10.000 topic.

Hỗ trợ Auto Topic từ:

```text
Project
Runtime
Device
Tag
```

Ví dụ:

```text
factory01/line01/tank01/level
```

Topic template phải configurable.

Cho phép override riêng khi cần.

---

# 31. MQTT payload

Payload phải đủ dùng cho Web.

Ít nhất nên truyền được:

```text
Tag
Value
Quality
Timestamp
```

Không chỉ gửi raw value mà mất Quality/Timestamp.

Payload format phải được định nghĩa tập trung.

---

# 32. MQTT Broker settings

Broker cấu hình một lần trong:

```text
Settings → MQTT
```

Ví dụ:

```text
Enabled
Host
Port
ClientId
Username
Password
BaseTopic
Reconnect
Test Connection
Status
```

Không đưa Broker config vào từng Tag.

---

# 33. MQTT Write

Kiến trúc có thể chừa đường cho:

```text
Web → MQTT → Runtime → PLC
```

nhưng:

```text
MQTT Write mặc định OFF.
```

Không implement web control đầy đủ ở V1 nếu chưa cần.

Tương lai command phải đi qua:

```text
Runtime validation
Permission
Writable check
Interlock
Device state
TagWriteService
```

Không bao giờ:

```text
Browser → PLC trực tiếp
```

---

# 34. Configuration UI design philosophy

Phần cấu hình WPF phải:

```text
khoa học
đẹp
gọn
dễ hiểu
không chiếm nhiều không gian
```

Không để màn hình cấu hình phá diện tích của Machine UI.

---

# 35. Operation vs Engineering

Phân biệt rõ:

```text
Operation
Engineering
```

Operation gồm:

```text
Overview
Machine/Process screens
Alarm
Trend
```

Engineering gồm:

```text
Devices
Tags
History
MQTT
System Services
Diagnostics
```

Machine Settings là một nhóm riêng, không đồng nhất với Engineering.

---

# 36. Configuration page pattern

Ưu tiên pattern:

```text
Toolbar
Search/Filter
Main Grid/List
Detail Panel
Advanced Collapse
```

Không bày mọi tùy chọn lên màn hình cùng lúc.

---

# 37. Design System

Phải có design system thống nhất:

```text
Spacing
Typography
Control height
Cards
Toolbar
Forms
DataGrid
Status colors/icons
```

Không để mỗi màn hình tự chọn style khác nhau.

Không sử dụng quá nhiều màu/gradient/effect.

Phong cách:

```text
industrial
clean
modern
readable
compact
```

---

# 38. System Services UI

Settings cần có vùng quản lý:

```text
History
MQTT
Local Buffer
System health
```

Operator chủ yếu thấy:

```text
Connected
Running
Healthy
Buffering
Disconnected
```

Engineer mới mở chi tiết.

---

# 39. SQLite UI

Khi Storage = SQLite chỉ hiện các thông tin liên quan local storage:

```text
Database path
Size
Retention
Status
Open folder
Advanced
```

Không hiện Server/User/Token không cần thiết.

---

# 40. InfluxDB UI

Chỉ khi Storage = InfluxDB mới hiện:

```text
Server
Port/URL
Organization
Bucket
Token
Test Connection
```

Dùng progressive disclosure.

---

# 41. MQTT UI

Nếu MQTT disabled:

```text
MQTT
Disabled
[Enable]
```

Khi enabled mới hiện broker settings.

Không chiếm diện tích khi không sử dụng.

---

# 42. Overview/System Health

Overview có summary nhỏ:

```text
PLC        50/50
Historian  Online
MQTT       Online
Database   Healthy

CPU
Memory
Runtime uptime
```

Không biến Overview thành diagnostics screen.

---

# 43. Status Bar

Status bar nhỏ có thể hiển thị:

```text
PLC ●
History ●
MQTT ●
Runtime ●
```

Nếu lỗi thì highlight trạng thái.

Không hiển thị log kỹ thuật dài ở status bar.

---

# 44. Machine UI do người triển khai tự thiết kế

Framework không tự sinh toàn bộ machine screen.

Người triển khai sử dụng Visual Studio/WPF để thiết kế:

```text
ProcessView.xaml
Line01View.xaml
Machine01View.xaml
...
```

Framework phải làm việc này dễ hơn bằng reusable controls.

---

# 45. Reusable HMI Controls

Cung cấp một thư viện control dùng lại:

```text
Motor
Pump
Valve
Fan
Conveyor
Mixer

Tank
Vessel
Hopper
Pipe

NumericDisplay
Indicator
Gauge
BarGraph
```

Không cần hoàn thiện tất cả ở milestone đầu.

Architecture phải cho phép bổ sung dần.

---

# 46. Control không biết PLC address

Ví dụ:

```text
MotorControl
```

nhận logical tag:

```text
RunTag
FaultTag
ReadyTag
AutoTag
```

Không biết:

```text
M100
D100
DB1.DBX...
```

UI bind logical Tag.

---

# 47. Reusable Faceplates

Mỗi equipment type có thể có Faceplate dùng chung.

Ví dụ:

```text
MotorFaceplate
PumpFaceplate
ValveFaceplate
AnalogFaceplate
```

50 pump không tạo 50 popup riêng.

Một faceplate nhận equipment/tag context.

---

# 48. Symbol assets

Phải hỗ trợ:

```text
Built-in assets
Custom assets
External assets
```

Cấu trúc ví dụ:

```text
Resources/
└── Symbols/
    ├── BuiltIn/
    ├── Custom/
    └── External/
```

---

# 49. External symbol libraries

Có thể sử dụng nguồn bên ngoài như:

```text
Symbol Factory
SVG libraries
XAML assets
custom drawings
```

Nhưng external library chỉ là:

```text
graphic source
```

Không được trở thành architecture dependency của toàn SCADA.

Đúng:

```text
External Symbol
      ↓
Reusable Scada Control
      ↓
Machine UI
```

Không:

```text
Machine UI
      ↓
Vendor API everywhere
```

Phải tránh vendor lock-in.

Tuân thủ licensing của library bên ngoài.

Không giả định được phép redistribute asset có license nếu chưa xác minh.

---

# 50. Multi-screen architecture

Dự án có thể có:

```text
5
20
50
100+
```

màn hình.

Không để tất cả View nằm lộn xộn trong một folder phẳng.

Tổ chức theo:

```text
Module
Line
Machine
```

Ví dụ:

```text
Modules/
├── Line01/
│   ├── Views/
│   ├── Settings/
│   └── ViewModels/
│
├── Line02/
│   ├── Views/
│   ├── Settings/
│   └── ViewModels/
│
└── Utilities/
```

---

# 51. Navigation groups

Navigation tổng thể:

```text
OPERATION
├── Overview
├── Line 01
├── Line 02
└── Utilities

MACHINE SETTINGS
├── Line 01
└── Line 02

MONITORING
├── Alarm
└── Trend

ENGINEERING
├── Devices
├── Tags
├── History
├── MQTT
├── System
└── Diagnostics
```

Không hiển thị 50–100 menu item cùng lúc.

Navigation cần hierarchical/collapsible phù hợp.

---

# 52. Machine Settings

Machine Settings khác Engineering Settings.

Machine Settings chứa:

```text
Speed
Timer
Delay
Setpoint
Recipe
Calibration
Production parameters
```

Engineering chứa:

```text
PLC
Tags
History
MQTT
Runtime
Database
```

Không trộn hai nhóm.

---

# 53. Reusable parameter UI

Machine Settings nên dùng reusable control/template khi có thể.

Ví dụ:

```text
ParameterEditor
ParameterGroup
```

Parameter có:

```text
Name
Value
Unit
Min
Max
Description
```

Không tự dựng lại cùng một form ở từng máy.

---

# 54. Screen metadata

Architecture nên chừa concept:

```text
ScreenId
Title
Category
Icon
Order
RequiredRole
```

để tương lai navigation/permission dễ mở rộng.

Không bắt buộc xây dynamic screen discovery phức tạp ở milestone đầu.

---

# 55. Deployment architecture

Phân biệt:

## Embedded / application dependency

```text
SQLite library
MQTT client library
InfluxDB client library
```

Các library cần thiết đi cùng build/publish.

## External optional services

```text
MQTT Broker
InfluxDB Server
```

Có thể chạy:

```text
cùng SCADA PC
hoặc
server riêng
```

---

# 56. External service failure

MQTT hoặc InfluxDB offline không được khiến toàn SCADA không chạy.

Ví dụ:

```text
PLC       OK
HMI       OK
Alarm     OK
MQTT      Offline
History   Buffering
```

Runtime phải degrade gracefully.

---

# 57. Deployment folder

Chuẩn bị:

```text
Deployment/
├── Publish scripts
├── Environment checks
├── Documentation
└── future installer support
```

Không cần xây installer phức tạp ngay milestone đầu.

---

# 58. Offline factory consideration

Architecture phải tránh giả định rằng PC engineering/SCADA luôn có Internet.

Source development bình thường có thể dùng NuGet restore.

Tài liệu cần ghi rõ strategy để về sau hỗ trợ:

```text
offline NuGet cache/package source
offline installation packages
```

Không cần đóng gói toàn bộ offline installer ngay V1 đầu tiên.

---

# 59. Copyability requirement

Đây là acceptance criterion quan trọng.

Phải có khả năng:

```text
SCADA_TEMPLATE
      ↓ Copy
TEST_PROJECT
```

Sau đó trong folder mới:

```text
open Scada.sln
restore
build
run
```

không phụ thuộc folder gốc.

Không yêu cầu rename toàn bộ namespace.

Project display name/customer/machine name lấy từ configuration.

---

# 60. Simulator

Template phải có Simulator.

Mục tiêu:

```text
Copy project
↓
không có PLC thật
↓
F5
↓
SCADA vẫn chạy
```

Simulator tạo dữ liệu deterministic/smooth cho:

```text
Analog
Boolean
Counter
Fault state
```

Không dùng random hoàn toàn khiến UI nhảy loạn.

---

# 61. Stress testing

Tạo khả năng stress test architecture ở quy mô:

```text
50 simulated PLC
10.000 tags
```

Theo dõi tối thiểu:

```text
CPU
RAM
scan duration
scan jitter
missed scan cycles
tag updates/sec
historian queue
UI responsiveness
```

Không đặt hard performance claim trước khi benchmark.

---

# 62. Documentation

Repository phải có ít nhất:

```text
docs/ARCHITECTURE.md
docs/PROJECT_STRUCTURE.md
docs/DEVELOPMENT_RULES.md
docs/ROADMAP.md
```

Tài liệu phải phản ánh implementation thật.

Mỗi thay đổi architecture/tính năng lớn phải cập nhật docs/spec tương ứng.

Không để source và docs lệch nhau.

---

# 63. Git / Worktree workflow

Trước khi sửa code:

1. Kiểm tra repository.
2. Kiểm tra branch.
3. Kiểm tra worktree hiện tại.
4. Không thao tác phá hủy worktree khác.
5. Không sửa nhầm branch/worktree.
6. Kiểm tra working tree có thay đổi chưa commit hay không.

Nếu source đã tồn tại:

* phân tích codebase trước;
* dùng GitNexus nếu có sẵn;
* xác định dependency và implementation hiện có;
* tránh tạo duplicate architecture.

---

# 64. AgentMemory / existing project context

Nếu repository đã có memory/specification/agent context thì phải đọc trước khi implement.

Không được bỏ qua các quyết định kiến trúc đã tồn tại.

---

# 65. Plan-before-code

Không bắt đầu code ngay.

Trước khi implement:

1. Phân tích repository.
2. Đọc docs/spec.
3. Xác định current state.
4. So sánh với Architecture V1 này.
5. Viết implementation plan.
6. Chỉ sau đó mới thay đổi source.

Plan phải chia nhỏ thành milestone/task rõ ràng.

---

# 66. Không implement tất cả V1 trong một thay đổi khổng lồ

Specification này mô tả target architecture.

Triển khai theo milestone.

Roadmap đã triển khai và tiếp tục theo thứ tự:

```text
Milestone 1
Foundation / solution / architecture

Milestone 2
Runtime and device polling

Milestone 3
WPF Shell + workspaces

Milestone 4
Tag Manager

Milestone 5
Historian foundation + SQLite

Milestone 6
InfluxDB provider + durable buffering

Milestone 7
MQTT Publisher

Milestone 8
Reusable Controls + Faceplates

Milestone 9
Machine Settings reusable parameter UI

Milestone 10
Performance/stress hardening

Milestone 11
PLC-read-only Alarm System
```

Có thể điều chỉnh milestone nếu dependency thực tế yêu cầu, nhưng không gom tất cả thành một lần.

---

# 67. Milestone 1 — phạm vi triển khai ngay

Trong lần implementation đầu tiên, tập trung vào foundation.

Tạo/chuẩn hóa:

```text
Scada.sln

Scada.Core
Scada.Runtime
Scada.Drivers
Scada.Infrastructure
Scada.App

tests/
docs/
scripts/
Deployment/
```

Milestone 1 phải chuẩn bị:

```text
domain models cơ bản
driver abstraction
RuntimeId
DI/composition
configuration foundation
Simulator foundation
TagCache foundation
basic WPF shell
basic navigation
project structure
documentation
```

Không cần hoàn thiện:

```text
real Siemens driver
real Mitsubishi driver
real Modbus driver
OPC UA
InfluxDB
full History Manager
full MQTT
full Alarm system
Trend
Reports
Recipes
web
multi-runtime networking
```

---

# 68. WPF Shell V1

Shell cần đủ để chứng minh navigation architecture:

```text
Header
Compact Navigation
Main Content
Status Bar
```

Nhóm navigation chuẩn bị:

```text
Operation
Machine Settings
Monitoring
Engineering
```

Không cần tạo hàng chục screen demo.

Một vài placeholder hợp lý là đủ.

---

# 69. Visual style V1

Ngay từ đầu tạo resource/style foundation để tránh hard-code style trong từng View.

Ví dụ:

```text
Colors
Typography
Spacing
Buttons
TextBox
ComboBox
DataGrid
Cards
Status indicator
```

Không cần thiết kế pixel-perfect toàn bộ SCADA trong milestone đầu.

Nhưng foundation phải thống nhất.

---

# 70. Coding principles

Ưu tiên:

```text
simple
explicit
testable
maintainable
observable
industrial-friendly
```

Tránh abstraction chỉ để abstraction.

Không tạo generic framework quá mức khi chưa có use case.

Không tạo class/interface vô nghĩa chỉ để “Clean Architecture”.

Mỗi abstraction phải có lý do.

---

# 71. Error handling

Không swallow exception im lặng.

Các external component cần trạng thái rõ ràng.

Ví dụ:

```text
PLC connection failure
Historian failure
MQTT failure
configuration error
```

phải log và expose health state phù hợp.

Không làm app crash toàn bộ chỉ vì optional service lỗi.

---

# 72. Logging

Chuẩn bị centralized logging.

Không dùng `Console.WriteLine` rải rác làm logging chính.

Log phải có context tối thiểu:

```text
RuntimeId
DeviceId nếu liên quan
component
severity
timestamp
```

---

# 73. Configuration validation

Configuration phải được validate trước khi Runtime chạy.

Ví dụ:

```text
duplicate tag
missing device
invalid scan group
unsupported datatype
invalid address
invalid profile
```

Không chờ Runtime chạy lâu rồi mới phát hiện.

---

# 74. Testing

Tối thiểu chuẩn bị unit tests cho các thành phần nền khi chúng xuất hiện:

```text
TagCache
TagEngine
ScanGroup scheduling
Simulator
configuration validation
```

Các milestone sau thêm tests tương ứng.

---

# 75. Verification mỗi milestone

Trước khi báo hoàn thành:

```text
restore
build
tests
```

phải pass.

Ngoài ra kiểm tra:

```text
no circular dependency
no absolute project paths
no duplicated architecture
docs updated
```

Nếu milestone tác động copyability:

```text
copy repository to test folder
restore
build
```

phải pass.

---

# 76. Báo cáo sau mỗi milestone

Báo cáo ngắn gọn nhưng đầy đủ:

1. Đã làm gì.
2. Những file/project chính thay đổi.
3. Dependency graph hiện tại.
4. Build result.
5. Test result.
6. Những phần chưa làm.
7. Technical debt/risk phát hiện.
8. Đề xuất milestone tiếp theo.

Không báo “done” nếu build/test chưa chạy.

---

# 77. Các yêu cầu chưa được phép tự ý thêm

Không tự ý thêm:

```text
distributed runtime
cloud architecture
microservices
Kubernetes
message bus ngoài MQTT
web frontend
Docker dependency bắt buộc
complex plugin marketplace
scripting engine
redundancy
HA cluster
```

nếu chưa có yêu cầu mới.

Architecture phải chừa khả năng mở rộng, nhưng V1 phải giữ đơn giản.

---

# 78. Quy tắc quan trọng nhất

Hãy luôn giữ trải nghiệm triển khai này:

```text
COPY TEMPLATE
     ↓
OPEN VISUAL STUDIO
     ↓
CONFIGURE PLC
     ↓
CONFIGURE TAG
     ↓
CHECK ONLINE TAGS
     ↓
DESIGN MACHINE UI
     ↓
RUN
```

Nếu một quyết định kiến trúc làm workflow này phức tạp lên đáng kể mà không đem lại lợi ích rõ ràng, hãy xem lại quyết định đó.

---

# 79. Bắt đầu công việc

Hãy thực hiện theo trình tự:

1. Inspect repository/worktree.
2. Phân tích source hiện tại bằng GitNexus nếu source đã tồn tại.
3. Đọc toàn bộ docs/spec hiện hữu.
4. So sánh source với Architecture V1 này.
5. Viết plan Milestone 1.
6. Xác định file nào sẽ tạo/sửa.
7. Implement Milestone 1.
8. Build.
9. Test.
10. Cập nhật documentation.
11. Báo cáo kết quả.

Không vượt sang các milestone sau trừ khi một phần foundation tối thiểu bắt buộc để Milestone 1 compile/run.

Architecture V1 này là baseline hiện tại. Các yêu cầu mới sẽ được bổ sung sau dưới dạng thay đổi specification, không tự suy diễn trước.


