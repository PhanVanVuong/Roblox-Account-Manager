'use strict';
let detectedStage = 'unknown';
let workerStatus = '';
function showStatus() {
  const legacyMissing = workerStatus.startsWith('Chưa tìm thấy ô:');
  document.getElementById('status').textContent = legacyMissing && ['profile', 'password'].includes(detectedStage)
    ? 'Đã nhận diện form nhiều bước. Bộ tự điền cũ chưa hỗ trợ bước này; bạn cần hoàn tất trực tiếp trong Chrome.'
    : workerStatus || 'Đang xử lý…';
}
async function refresh(type = 'status') {
  try { const data = await chrome.runtime.sendMessage({type}); workerStatus = data.status || ''; showStatus(); }
  catch { document.getElementById('status').textContent = 'Hãy tải lại tiện ích rồi thử lại.'; }
}
document.getElementById('connect').addEventListener('click', () => refresh('connect'));
document.getElementById('skip').addEventListener('click', () => refresh('skip'));
void refresh('connect');
setInterval(refresh, 1500);
let scanning = false;
async function scanFields() {
  if (scanning) return;
  scanning = true;
  const output = document.getElementById('fields');
  try {
    const [tab] = await chrome.tabs.query({active: true, lastFocusedWindow: true});
    const url = new URL(tab?.url || 'about:blank');
    if (url.protocol !== 'https:' || url.hostname !== 'www.roblox.com') {
      detectedStage = 'unknown'; showStatus();
      output.textContent = 'Mở trang Roblox để nhận diện các ô nhập.'; return;
    }
    const results = await chrome.scripting.executeScript({target: {tabId: tab.id}, files: ['field-scan.js']});
    const fields = results[0]?.result || {};
    detectedStage = fields.stage || 'unknown'; showStatus();
    const labels = {username: 'Username', password: 'Mật khẩu', day: 'Ngày', month: 'Tháng', year: 'Năm', birthday: 'Ngày sinh', gender: 'Giới tính'};
    const step = fields.stage === 'full' ? 'Form đầy đủ: có username, mật khẩu và ngày sinh. ' : fields.stage === 'profile' ? 'Bước thông tin tài khoản: mật khẩu chưa xuất hiện ở bước này. Bạn hoàn tất và bấm Tiếp tục trong Chrome. ' : fields.stage === 'password' ? 'Bước thêm mật khẩu: bạn hoàn tất trong Chrome. ' : 'Chưa xác định chắc chắn loại form. ';
    if (!fields.birthday && fields.day && fields.month && fields.year) delete labels.birthday;
    output.textContent = step + Object.entries(labels).map(([key, label]) => label + ': ' + (fields[key] > 0 ? 'tìm thấy (' + fields[key] + ')' : key === 'password' && fields.stage === 'profile' ? 'ở bước sau' : 'chưa thấy')).join(' · ');
  } catch { detectedStage = 'unknown'; showStatus(); output.textContent = 'Chưa quét được. Kiểm tra quyền truy cập trang và quyền chạy ẩn danh của tiện ích.'; }
  finally { scanning = false; }
}
void scanFields();
setInterval(scanFields, 2000);
