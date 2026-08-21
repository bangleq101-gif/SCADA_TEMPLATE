# SCADA_TEMPLATE — Agent Instructions

## 1. Source of Truth

Trước khi thực hiện bất kỳ thay đổi nào, phải đọc:

* `docs/SCADA_ARCHITECTURE_V1.md`
* `docs/CURRENT_STATE.md`
* `docs/ROADMAP.md`
* `docs/DECISIONS.md`

`docs/SCADA_ARCHITECTURE_V1.md` là specification kiến trúc chính thức hiện tại của dự án.

Nếu source code và specification có mâu thuẫn, không tự ý thay đổi kiến trúc. Phải phân tích nguyên nhân và ưu tiên thay đổi nhỏ nhất phù hợp với specification.

---

## 2. Workflow bắt buộc trước khi sửa code

Trước mỗi feature, bug fix hoặc refactor:

1. Kiểm tra Git repository hiện tại.
2. Kiểm tra branch hiện tại.
3. Kiểm tra Git worktree hiện tại.
4. Kiểm tra `git status`.
5. Không sửa nhầm branch hoặc worktree khác.
6. Không xóa hoặc làm hỏng thay đổi chưa commit của người dùng.
7. Đọc specification liên quan.
8. Nếu repository đã có source, dùng GitNexus để phân tích codebase, dependency và impact trước khi chỉnh sửa.
9. Kiểm tra implementation hiện có để tránh tạo code hoặc abstraction trùng lặp.
10. Lập implementation plan trước khi viết code.

Không bắt đầu implement ngay khi chưa hiểu phần source bị ảnh hưởng.

---

## 3. Plan Before Implementation

Mọi feature hoặc bug fix có phạm vi đáng kể phải có plan trước.

Plan tối thiểu phải xác định:

* mục tiêu;
* phần source bị ảnh hưởng;
* dependency liên quan;
* file dự kiến tạo/sửa;
* cách triển khai;
* test cần thực hiện;
* rủi ro hoặc backward compatibility nếu có.

Ưu tiên thay đổi nhỏ, rõ ràng và có thể kiểm chứng.

Không thực hiện refactor lớn ngoài phạm vi task nếu không thực sự cần thiết.

---

## 4. Kiến trúc không được phá

Giữ các nguyên tắc sau:

### Project structure

```text
Scada.Core
Scada.Runtime
Scada.Drivers
Scada.Infrastructure
Scada.App
```

### Runtime independence

`Scada.Runtime` không được phụ thuộc WPF hoặc `Scada.App`.

### PLC data flow

UI không được đọc PLC trực tiếp.

Luồng chuẩn:

```text
PLC
 ↓
Driver
 ↓
Polling / Batch Read
 ↓
TagCache
 ├── UI
 ├── Alarm
 ├── Historian
 └── MQTT
```

### TagCache

`TagCache` là nguồn dữ liệu runtime trung tâm.

Không để UI, Historian, Alarm hoặc MQTT tự đọc lại PLC.

### Performance

Hệ thống phải giữ khả năng mở rộng cho:

* nhiều loại PLC;
* hàng chục PLC;
* khoảng 10.000 tags.

Các nguyên tắc bắt buộc:

* Batch Read;
* Scan Groups;
* asynchronous device isolation;
* central TagCache;
* subscription-based UI updates.

### Historian

Historian/database không được block PLC polling.

Sử dụng queue/background writer phù hợp.

### MQTT

MQTT publish dữ liệu từ TagCache.

MQTT không được tạo thêm PLC reads.

MQTT Write mặc định OFF.

### Multi-runtime

V1 chỉ sử dụng một Runtime.

Có thể chuẩn bị khả năng mở rộng trong tương lai nhưng không tự ý triển khai:

* distributed runtime;
* gRPC;
* SignalR runtime communication;
* runtime discovery;
* multi-runtime networking.

---

## 5. Copy-folder portability

Một yêu cầu nền của dự án là:

```text
SCADA_TEMPLATE
      ↓ copy
NEW_PROJECT
      ↓
open Scada.sln
      ↓
restore / build / run
```

Không được tạo dependency vào đường dẫn tuyệt đối như:

```text
C:\...
D:\...
```

Không phụ thuộc source/library nội bộ nằm ngoài repository.

Project references phải dùng relative path.

Không yêu cầu rename toàn bộ namespace khi copy project.

---

## 6. UI Principles

Phần UI phải tuân theo:

* WPF;
* MVVM;
* reusable controls;
* reusable faceplates;
* logical Tag binding;
* không bind trực tiếp PLC address vào Machine UI.

Configuration UI phải:

* gọn;
* khoa học;
* dễ sử dụng;
* không chiếm quá nhiều không gian;
* Advanced options mặc định collapse;
* hỗ trợ DataGrid virtualization khi dữ liệu lớn.

Phân biệt:

```text
Operation
Machine Settings
Monitoring
Engineering
```

Không trộn Machine Settings với Engineering Settings.

---

## 7. Tag Manager

Tag Manager phải hướng đến cấu hình trực quan.

Hỗ trợ hoặc chuẩn bị kiến trúc cho:

* Add;
* Delete;
* Edit;
* Duplicate;
* Search;
* Filter;
* Sort;
* Multi-select;
* Bulk edit;
* Copy/Paste;
* Import/Export;
* History configuration;
* MQTT configuration;
* khoảng 10.000 tags.

Không bắt người dùng bình thường chỉnh `tags.json` thủ công.

---

## 8. External libraries

Có thể sử dụng thư viện ngoài khi mang lại giá trị rõ ràng.

Ví dụ:

* NuGet packages;
* Symbol Factory;
* SVG/XAML symbol libraries;
* PLC libraries.

Nhưng không được làm kiến trúc SCADA phụ thuộc chặt vào một vendor nếu có thể tránh.

External symbol libraries chỉ nên là nguồn graphic.

Reusable SCADA Control phải là abstraction mà Machine UI sử dụng.

Không giả định quyền redistribute asset/library có license nếu chưa xác minh.

---

## 9. Documentation

Khi implementation làm thay đổi behavior hoặc architecture, phải cập nhật tài liệu tương ứng.

Các file chính:

```text
docs/SCADA_ARCHITECTURE_V1.md
docs/CURRENT_STATE.md
docs/ROADMAP.md
docs/DECISIONS.md
```

Không để documentation và implementation lệch nhau.

Các quyết định kiến trúc quan trọng phải được ghi vào `docs/DECISIONS.md`.

Tiến độ thực tế phải được cập nhật trong `docs/CURRENT_STATE.md`.

---

## 10. Build và Test

Sau mỗi thay đổi đáng kể phải chạy tối thiểu:

```powershell
dotnet restore
dotnet build
dotnet test
```

Nếu solution/project chưa tồn tại ở milestone hiện tại thì thực hiện các bước phù hợp với trạng thái repository.

Không báo task hoàn thành nếu build hoặc test đang fail mà chưa giải thích rõ.

---

## 11. Verification

Trước khi kết luận hoàn thành task, kiểm tra:

* build thành công;
* tests liên quan pass;
* không circular dependency;
* không absolute path mới;
* không duplicate implementation;
* không phá copy-folder portability;
* documentation đã cập nhật nếu cần.

Với thay đổi liên quan Runtime/performance, bổ sung test hoặc benchmark phù hợp.

---

## 12. Git Rules

Không làm việc trực tiếp trên `main` cho feature lớn nếu workflow hiện tại sử dụng feature branch/worktree.

Không:

* force push nếu không được yêu cầu;
* reset phá hủy thay đổi của người dùng;
* xóa branch/worktree khác;
* commit secret;
* commit build output;
* commit local runtime database/log không cần thiết.

Luôn kiểm tra worktree hiện tại trước khi chỉnh sửa.

---

## 13. Scope Control

Không tự ý thêm các hệ thống lớn chưa được yêu cầu, ví dụ:

* multi-runtime distributed architecture;
* web frontend;
* cloud platform;
* Kubernetes;
* microservices;
* redundancy/HA;
* scripting engine;
* plugin marketplace.

Architecture có thể chừa khả năng mở rộng nhưng implementation phải bám đúng milestone hiện tại.

---

## 14. Coding Style

Ưu tiên:

* đơn giản;
* dễ đọc;
* dễ debug;
* dễ test;
* dependency rõ ràng;
* phù hợp ứng dụng công nghiệp chạy lâu dài.

Không tạo abstraction chỉ để làm kiến trúc trông phức tạp hơn.

Không over-engineer.

Không để logic PLC nằm trong code-behind của WPF nếu có thể đặt đúng layer.

---

## 15. Khi hoàn thành một task

Báo cáo:

1. Đã thay đổi gì.
2. Các file/project chính đã sửa.
3. Build result.
4. Test result.
5. Những phần chưa triển khai.
6. Technical debt hoặc rủi ro phát hiện.
7. Bước tiếp theo được đề xuất.

Nếu task chưa hoàn tất hoàn toàn, nói rõ phần nào còn thiếu.

---

## 16. Nguyên tắc cuối cùng

Luôn bảo vệ workflow sử dụng chính của dự án:

```text
COPY TEMPLATE
     ↓
OPEN VISUAL STUDIO
     ↓
CONFIGURE PLC
     ↓
CONFIGURE TAGS
     ↓
CHECK ONLINE DATA
     ↓
DESIGN MACHINE UI
     ↓
RUN
```

Nếu một giải pháp làm workflow này phức tạp đáng kể mà không mang lại lợi ích kỹ thuật rõ ràng, hãy chọn giải pháp đơn giản hơn.

## 17. Initial planning gate

Khi repository chưa có production SCADA source code, milestone đầu tiên phải bắt đầu bằng việc kiểm tra repository, branch, worktree và toàn bộ tài liệu trong `docs/`.

Trước khi tạo bất kỳ source code nào cho Milestone 1 — Foundation, phải:

1. Xác nhận đang làm việc trên `feature/milestone-1-foundation`.
2. Đọc đầy đủ `SCADA_ARCHITECTURE_V1.md`, `CURRENT_STATE.md`, `ROADMAP.md` và `DECISIONS.md`.
3. Phân tích dependency architecture.
4. Lập implementation plan chi tiết.
5. Liệt kê project/file dự kiến tạo.
6. Nêu rõ các phần không triển khai trong milestone.

Chỉ trả về plan để review. Không implement source code cho đến khi plan được người dùng phê duyệt.
