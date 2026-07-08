# Hướng dẫn setup Minigame 2 (Server + SSD) — bản đèn báo đỏ/xanh + âm thanh

## 1. Những gì đã thay đổi trong code

**ServerBlock.cs**
- Bỏ hoàn toàn `blockRenderer` (đổi màu nguyên cả khối server).
- Thêm `statusLights` (mảng `Renderer[]`) — kéo nhiều đèn báo trên thân server vào đây.
  Đỏ khi chưa gắn SSD, xanh khi gắn xong.
- Thêm `AudioSource` + `AudioClip insertSSDSound` — phát khi gắn SSD thành công.

**ServerMinigameManager.cs**
- Thêm `AudioSource` + `AudioClip riseSound` — phát mỗi lần 1 khối server trồi lên.

---

## 2. Chuẩn bị model: tách đèn báo thành các Renderer riêng

Nhìn model của bạn, mỗi tầng ổ có 2 chi tiết:
- 1 ô đèn nhỏ hình vuông (bên trái)
- 1 thanh đèn dài màu xanh lá (bên phải)

Để script điều khiển được, **mỗi đèn phải là 1 GameObject/mesh riêng có Renderer**, không được gộp chung 1 mesh với thân server. Nếu model hiện tại đang là 1 mesh liền:

- Cách nhanh nhất: trong phần mềm 3D (Blender) hoặc ngay trong Unity nếu model đã có sẵn submesh, tách các đèn ra thành object con, mỗi đèn 1 Material slot riêng (VD: `Mat_Light_01`, `Mat_Light_02`...).
- Nếu server có 8 tầng như trong ảnh → bạn sẽ có 8 (hoặc 16 nếu tính cả ô vuông + thanh dài) Renderer để kéo vào `statusLights`.

> Không bắt buộc phải tách hết — bạn có thể chỉ dùng thanh đèn dài màu xanh làm đèn báo chính, để ô vuông nhỏ giữ nguyên màu cố định cho đẹp.

---

## 3. Setup Material cho đèn (QUAN TRỌNG)

Vì script dùng `MaterialPropertyBlock` để đổi màu + emission (phát sáng), material gốc của đèn phải **bật sẵn Emission** trong Editor trước:

1. Chọn material của đèn (VD `Mat_Light_01`) trong Project.
2. Inspector → tick chọn **Emission** (Standard shader) hoặc mở phần **Emission** và bật (URP/Lit shader).
3. Set màu Emission bất kỳ (VD trắng), không cần đúng màu — code sẽ ghi đè lúc runtime.
4. Nếu dùng URP: đảm bảo shader là `Universal Render Pipeline/Lit`, project **Lighting → bật Bloom** trong Volume Profile để đèn thực sự "glow" nhìn rõ đỏ/xanh.

> Nếu bỏ qua bước bật Emission trong material gốc, `_EmissionColor` set qua code lúc runtime sẽ không hiển thị vì keyword shader chưa được compile.

---

## 4. Setup từng ServerBlock trong Scene

Với mỗi khối server (6-8 khối tuỳ scene):

1. Chọn GameObject server → component `ServerBlock`.
2. Kéo tất cả Renderer đèn (đã tách ở bước 2) vào field **Status Lights** (kéo nhiều cùng lúc bằng cách chọn hết rồi kéo vào field mảng).
3. Chỉnh **Empty Color** = đỏ, **Filled Color** = xanh (mặc định đã đúng, chỉnh lại nếu muốn tông màu khác).
4. **Emission Intensity**: mặc định 2.5, tăng lên nếu muốn đèn sáng rực hơn (VD 4-5), giảm nếu chói quá.
5. Kéo file âm thanh gắn SSD vào **Insert SSD Sound** (định dạng .wav/.mp3, khuyên dùng .wav ngắn, nhẹ).
6. **Audio Source**: để trống — script tự thêm AudioSource lúc Play (spatialBlend = 1, tức âm thanh 3D theo vị trí). Nếu muốn chỉnh thủ công (volume, rolloff, min/max distance), tự thêm AudioSource vào GameObject trước rồi kéo vào field này.
7. `interactHint` / `noSSDHint` giữ nguyên như cũ (UI Text/icon "[E] Gắn SSD" và "[!] Cần SSD").

---

## 5. Setup ServerMinigameManager

1. Chọn GameObject "ServerManager".
2. Kéo âm thanh trồi server vào **Rise Sound**.
3. Không cần thêm AudioSource thủ công — script tự thêm nếu bỏ trống, y như ServerBlock.
4. **Rise Sfx Volume**: chỉnh âm lượng (0-1). Vì mỗi khối trồi có `riseDelay` giữa các khối, âm thanh sẽ phát liên tiếp từng khối một — nghe như dây chuyền server đang khởi động, khá hợp không khí.

> Nếu bạn muốn CHỈ 1 âm thanh trồi lên duy nhất (không lặp lại theo từng khối), có thể chuyển lệnh `PlayRiseSound()` ra ngoài vòng lặp, gọi 1 lần trước khi `StartCoroutine(RiseAllBlocks())`. Nói mình biết nếu muốn bản đó, mình sửa lại ngay.

---

## 6. Test nhanh checklist

- [ ] Vào Play mode, bước vào trigger zone → nghe âm thanh trồi lên theo từng khối, đèn các khối đang màu đỏ.
- [ ] Nhặt SSD, lại gần 1 khối server, bấm E → đèn khối đó chuyển xanh + nghe âm thanh gắn SSD.
- [ ] Gắn đủ tất cả khối → cửa mở như cũ (logic `CheckAllFilled` / `SolveSequence` không đổi).
- [ ] Nếu đèn không đổi màu: kiểm tra lại bước 3 (Emission chưa bật trong material gốc) hoặc `statusLights` chưa kéo đủ Renderer.
- [ ] Nếu không nghe âm thanh: kiểm tra AudioClip đã kéo vào đúng field, và AudioListener có tồn tại trong scene (thường gắn ở Main Camera).

---

## 7. Ghi chú thêm

- Đã bỏ hết các dòng `Debug.Log` spam mỗi frame trong `Update()` của `ServerBlock` và `SSDItem` (log liên tục mỗi frame làm nặng Console, không cần thiết khi đã chạy ổn). Log quan trọng (nhặt SSD, gắn SSD, hoàn tất) vẫn giữ nguyên.
- `PlayerInventory.cs`, `SSDItem.cs`, `TriggerRelay.cs`, `FaceCamera.cs` không cần đổi gì thêm cho phần đèn báo/âm thanh này — vẫn tương thích với 2 file mới.
