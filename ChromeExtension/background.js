'use strict';
const HOST = 'com.ram.chrome_bridge';
let port, serial = 0, busy = false, session, active = null;
let reconnectTimer;
let status = 'Đang tự kết nối với Roblox Account Manager.';
const pending = new Map();
const ready = chrome.storage.session.get(['clientSession', 'active']).then(data => {
  session = data.clientSession || crypto.randomUUID();
  active = data.active || null;
  return chrome.storage.session.set({clientSession: session});
});
function connect() {
  if (port) return;
  clearTimeout(reconnectTimer);
  try {
    port = chrome.runtime.connectNative(HOST);
    port.onMessage.addListener(message => {
      const request = pending.get(message.requestId);
      if (!request) return;
      clearTimeout(request.timer); pending.delete(message.requestId); request.resolve(message);
    });
    port.onDisconnect.addListener(() => {
      void chrome.runtime.lastError;
      port = null;
      status = 'Đang tự kết nối lại. Hãy mở Roblox Account Manager.';
      for (const request of pending.values()) { clearTimeout(request.timer); request.reject(new Error('Disconnected')); }
      pending.clear();
      scheduleReconnect();
    });
  } catch { status = 'Đang chờ bộ kết nối của ứng dụng.'; scheduleReconnect(); }
}
function scheduleReconnect() {
  clearTimeout(reconnectTimer);
  reconnectTimer = setTimeout(() => { connect(); void tick(); }, 5000);
}
async function rpc(command, data = {}) {
  await ready;
  if (!port) throw new Error('Disconnected');
  const requestId = ++serial;
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => { pending.delete(requestId); reject(new Error('Timeout')); }, 50000);
    pending.set(requestId, {resolve, reject, timer});
    port.postMessage({command, session, requestId, signupSupported: true, genderSupported: true, ...data});
  });
}
async function setActive(value) {
  active = value;
  // Only job/tab/window identifiers and progress flags are stored, never credentials or cookies.
  await chrome.storage.session.set({active: value});
}
async function closeOwnedWindow() {
  const previous = active;
  await setActive(null);
  if (previous?.windowId != null) {
    try { await chrome.windows.remove(previous.windowId); } catch { /* Already closed. */ }
  }
}
async function report(jobId, message) {
  status = message;
  return rpc('status', {jobId, message});
}
function loginPage(url) {
  try { const u = new URL(url); return u.protocol === 'https:' && u.hostname === 'www.roblox.com' && /^\/(?:[a-z]{2}(?:-[a-z]{2})?\/)?login\/?$/i.test(u.pathname); }
  catch { return false; }
}
// This function runs only in the managed Roblox tab, in Chrome's isolated extension world.
async function fillLogin(username, password, manual) {
  const wait = ms => new Promise(resolve => setTimeout(resolve, ms));
  let user, pass, button;
  for (let i = 0; i < 100; i++) {
    user = document.querySelector('#login-username'); pass = document.querySelector('#login-password'); button = document.querySelector('#login-button');
    if (user && pass && button) break;
    await wait(200);
  }
  if (!user || !pass || !button) return {ready: false};
  if (manual) {
    if (!button.dataset.ramCapture) {
      button.dataset.ramCapture = 'true';
      const capture = () => chrome.runtime.sendMessage({type: 'capture', password: pass.value}).catch(() => {});
      button.addEventListener('click', capture, true);
      pass.addEventListener('keydown', e => { if (e.key === 'Enter') capture(); }, true);
    }
    return {ready: true};
  }
  const setValue = (input, value) => {
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(input, value);
    input.dispatchEvent(new Event('input', {bubbles: true}));
    input.dispatchEvent(new Event('change', {bubbles: true}));
  };
  setValue(user, username); setValue(pass, password);
  for (let i = 0; i < 30 && button.disabled; i++) await wait(100);
  if (button.disabled) return {ready: false};
  button.click();
  return {ready: true};
}
function signupPage(url) {
  try {
    const u = new URL(url);
    if (u.protocol !== 'https:' || u.hostname !== 'www.roblox.com') return false;
    const path = u.pathname.replace(/\/$/, '');
    return path === '' || /^\/[a-z]{2}(?:-[a-z]{2})?(?:\/(?:signup|CreateAccount))?$/i.test(path) || /^\/(?:signup|CreateAccount)$/i.test(path);
  }
  catch { return false; }
}
async function fillSignup(username, password, birthday, gender = '') {
  const user = document.querySelector('#signup-username');
  const pass = document.querySelector('#signup-password');
  const month = document.querySelector('#MonthDropdown');
  const day = document.querySelector('#DayDropdown');
  const year = document.querySelector('#YearDropdown');
  const button = document.querySelector('#signup-button');
  const missing = [[user, 'username'], [pass, 'password'], [month, 'tháng'], [day, 'ngày'], [year, 'năm'], [button, 'nút đăng ký']].filter(([element]) => !element).map(([, name]) => name);
  if (missing.length) return {ready: false, error: 'Chưa tìm thấy ô: ' + missing.join(', ') + '. Trang có thể chưa tải xong hoặc đã đổi giao diện.'};
  if (!['', 'female', 'male'].includes(gender)) return {ready: false};
  const genderButton = gender ? document.querySelector(gender === 'female' ? '#FemaleButton' : '#MaleButton') : null;
  if (gender && (!genderButton || genderButton.disabled)) return {ready: false, error: 'Chưa tìm thấy nút giới tính đã chọn hoặc nút chưa sẵn sàng.'};
  const parts = String(birthday || '').split('-').map(Number);
  if (parts.length !== 3 || parts.some(n => !Number.isInteger(n) || n <= 0)) return {ready: false};
  const date = new Date(Date.UTC(parts[0], parts[1] - 1, parts[2]));
  if (date.getUTCFullYear() !== parts[0] || date.getUTCMonth() + 1 !== parts[1] || date.getUTCDate() !== parts[2]) return {ready: false, error: 'Ngày sinh không hợp lệ.'};
  const set = (element, value, prototype) => {
    Object.getOwnPropertyDescriptor(prototype, 'value').set.call(element, value);
    element.dispatchEvent(new Event('input', {bubbles: true}));
    element.dispatchEvent(new Event('change', {bubbles: true}));
    element.dispatchEvent(new Event('blur', {bubbles: true}));
  };
  const monthNames = ['january','february','march','april','may','june','july','august','september','october','november','december'];
  const optionNumber = (text, isMonth) => {
    const value = String(text || '').trim().toLowerCase();
    if (/^\d+$/.test(value)) return Number(value);
    if (isMonth) {
      const named = monthNames.findIndex(name => value === name || value === name.slice(0, 3));
      if (named >= 0) return named + 1;
      const localized = /^(?:tháng|thang|month)\s*(\d{1,2})$/.exec(value);
      if (localized) return Number(localized[1]);
    }
    return null;
  };
  for (const [selector, number, label] of [['#YearDropdown', parts[0], 'năm'], ['#MonthDropdown', parts[1], 'tháng'], ['#DayDropdown', parts[2], 'ngày']]) {
    // Read fresh controls after change handlers potentially rebuild the birthday fields.
    const element = document.querySelector(selector);
    const options = Array.from(element?.options || []).filter(item => !item.disabled && String(item.value).trim() !== '');
    const isMonth = selector === '#MonthDropdown';
    const option = options.find(item => optionNumber(item.textContent || item.label, isMonth) === number) ||
      options.find(item => optionNumber(item.value, isMonth) === number);
    if (!option) return {ready: false, error: 'Không tìm thấy ' + label + ' sinh đã chọn trên trang.'};
    set(element, option.value, HTMLSelectElement.prototype);
    await new Promise(resolve => setTimeout(resolve, 0));
  }
  set(document.querySelector('#signup-username'), username, HTMLInputElement.prototype);
  set(document.querySelector('#signup-password'), password, HTMLInputElement.prototype);
  if (genderButton && genderButton.getAttribute('aria-pressed') !== 'true' && !genderButton.classList.contains('gender-selected')) genderButton.click();
  // Give Roblox's own validation time to finish. Never repeat submissions or interact with challenges.
  for (let i = 0; i < 30 && button.disabled; i++) await new Promise(resolve => setTimeout(resolve, 100));
  if (!button.disabled) button.click();
  return {ready: true, submitted: !button.disabled};
}
async function tick() {
  if (busy || !port) return;
  busy = true;
  try {
    await ready;
    if (!await chrome.extension.isAllowedIncognitoAccess()) {
      status = 'Mở Chi tiết của tiện ích và bật Cho phép ở chế độ ẩn danh.'; return;
    }
    const response = await rpc('next');
    if (!response.ok) { status = response.error || 'Chưa kết nối được ứng dụng.'; return; }
    const job = response.job;
    if (!job) {
      if (active) await closeOwnedWindow();
      status = 'Đã kết nối. Nhập User:Pass trong Roblox Account Manager để bắt đầu.'; return;
    }
    if (active && active.id !== job.id) await closeOwnedWindow();
    if (!active) {
      const windows = await chrome.windows.getAll();
      if (windows.some(window => window.incognito)) {
        await report(job.id, 'Đóng các cửa sổ ẩn danh đang mở trước khi bắt đầu. Tiện ích không tự đóng cửa sổ của bạn.'); return;
      }
      const window = await chrome.windows.create({incognito: true, focused: true, url: job.signup ? 'https://www.roblox.com/vi/CreateAccount' : 'https://www.roblox.com/vi/login'});
      const tab = window.tabs?.[0] || (await chrome.tabs.query({windowId: window.id}))[0];
      if (!tab?.incognito) throw new Error('No private tab');
      await setActive({id: job.id, windowId: window.id, tabId: tab.id, filled: false});
    }
    let tab;
    try { tab = await chrome.tabs.get(active.tabId); }
    catch { await rpc('skip', {jobId: job.id}); await closeOwnedWindow(); return; }
    if (!tab.incognito || tab.windowId !== active.windowId) throw new Error('Wrong tab');
    const privateWindows = (await chrome.windows.getAll({populate: true})).filter(window => window.incognito);
    if (privateWindows.some(window => window.id !== active.windowId || window.tabs?.some(item => item.id !== active.tabId))) {
      await report(job.id, 'Đóng các cửa sổ hoặc tab ẩn danh khác để tránh dùng chung phiên đăng nhập.'); return;
    }
    const heartbeat = await report(job.id, job.signup ? 'Đang đăng ký ' + job.username + '. Kiểm tra thông báo trên trang và hoàn tất xác minh trong Chrome.' : job.manual ? 'Đăng nhập trong cửa sổ Chrome vừa mở; tài khoản sẽ tự lưu.' : 'Đang đăng nhập ' + job.username + '. Hoàn tất CAPTCHA/2FA nếu được yêu cầu.');
    if (heartbeat.cancelled) { await closeOwnedWindow(); return; }
    if (job.signup && !active.filled && tab.status === 'complete' && signupPage(tab.url)) {
      const results = await chrome.scripting.executeScript({target: {tabId: active.tabId}, func: fillSignup, args: [job.username, job.password, job.birthday, job.gender || '']});
      if (results[0]?.result?.ready) await setActive({...active, filled: true});
      await report(job.id, results[0]?.result?.error || (results[0]?.result?.ready ? 'Đã điền thông tin. Kiểm tra thông báo của Roblox và hoàn tất xác minh trong Chrome.' : 'Chưa điền được form đăng ký; đang chờ trang tải.'));
    }
    if (!job.signup && !active.filled && tab.status === 'complete' && loginPage(tab.url)) {
      const results = await chrome.scripting.executeScript({target: {tabId: active.tabId}, func: fillLogin, args: [job.username, job.password, job.manual]});
      if (results[0]?.result?.ready) await setActive({...active, filled: true});
    }
    const stores = await chrome.cookies.getAllCookieStores();
    const store = stores.find(item => item.tabIds.includes(active.tabId));
    if (!store) return;
    const cookie = await chrome.cookies.get({url: 'https://www.roblox.com/', name: '.ROBLOSECURITY', storeId: store.id});
    if (!cookie?.value) return;
    const result = await rpc('complete', {jobId: job.id, cookie: cookie.value});
    if (result.ok || result.cancelled) { await closeOwnedWindow(); status = result.ok ? 'Đã lưu tài khoản. Đang chuyển sang tài khoản tiếp theo.' : 'Đã hủy.'; }
    else status = result.error || 'Chưa lưu được tài khoản; sẽ thử lại.';
  } catch { status = 'Đang chờ Chrome hoặc ứng dụng phản hồi. Kiểm tra mạng; bấm Kết nối nếu đã ngắt.'; }
  finally { busy = false; }
}
chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message.type === 'capture') {
    if (active && sender.tab?.id === active.tabId && sender.tab.incognito && loginPage(sender.url)) {
      rpc('credentials', {jobId: active.id, password: String(message.password || '').slice(0, 5000)}).then(() => sendResponse({ok: true})).catch(() => sendResponse({ok: false}));
      return true;
    }
    sendResponse({ok: false}); return;
  }
  if (sender.tab && !sender.url?.startsWith(chrome.runtime.getURL(''))) return;
  if (message.type === 'connect') { connect(); void tick(); }
  if (message.type === 'skip') {
    (async () => { if (active) { await rpc('skip', {jobId: active.id}); await closeOwnedWindow(); } sendResponse({ok: true}); })().catch(() => sendResponse({ok: false}));
    return true;
  }
  sendResponse({status, connected: !!port});
});
chrome.runtime.onStartup.addListener(connect);
chrome.runtime.onInstalled.addListener(connect);
chrome.alarms.onAlarm.addListener(alarm => {
  if (alarm.name === 'ram-reconnect') { connect(); void tick(); }
});
void chrome.alarms.create('ram-reconnect', {periodInMinutes: 1});
setInterval(tick, 2500);
connect();
