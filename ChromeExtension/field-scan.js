// Read-only: inspect control labels; return categories only, never input values.
(() => {
  const normalize = text => String(text || '').normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/đ/g, 'd').toLowerCase();
  const found = {username: 0, password: 0, day: 0, month: 0, year: 0, birthday: 0, gender: 0};
  let hasContinue = false, hasAddPassword = false;
  for (const element of document.querySelectorAll('input, select, button, [role="combobox"], [role="radio"]')) {
    if (element.type === 'hidden' || element.getClientRects().length === 0) continue;
    const style = getComputedStyle(element);
    if (style.visibility === 'hidden' || style.display === 'none') continue;
    const labels = Array.from(element.labels || []).map(label => label.textContent);
    const refs = (element.getAttribute('aria-labelledby') || '').split(/\s+/).filter(Boolean).map(id => document.getElementById(id)?.textContent || '');
    const role = element.getAttribute('role');
    const button = element.tagName === 'BUTTON' || role === 'radio';
    const caption = button || role === 'combobox' ? (element.textContent || '').trim() : '';
    const text = normalize([element.id, element.name, element.getAttribute('aria-label'), element.getAttribute('placeholder'), caption, ...labels, ...refs].join(' '));
    const dateControl = element.tagName === 'SELECT' || role === 'combobox' || element.getAttribute('aria-haspopup') === 'listbox' || /^(năm|year)$/i.test(caption);
    if (button && /^(continue|next|tiep tuc)$/.test(normalize(caption))) hasContinue = true;
    if (button && /^(add password|them mat khau)$/.test(normalize(caption))) hasAddPassword = true;
    const autocomplete = element.getAttribute('autocomplete') || '';
    if (element.type === 'password' || /(?:new|current)-password/.test(autocomplete)) { found.password++; continue; }
    if (element.tagName !== 'BUTTON' && (autocomplete === 'username' || /username|user-name|ten nguoi dung|ten dang nhap/.test(text))) found.username++;
    if (element.tagName !== 'BUTTON' && /password|mat khau/.test(text)) found.password++;
    if (element.type === 'date' || autocomplete === 'bday') found.birthday++;
    else {
      if (autocomplete === 'bday-day' || /daydropdown|\bday\b|\bngay\b/.test(text)) found.day++;
      if (autocomplete === 'bday-month' || /monthdropdown|\bmonth\b|\bthang\b/.test(text)) found.month++;
      if (autocomplete === 'bday-year' || /yeardropdown|\byear\b|nam sinh/.test(text) || (dateControl && /\bnam\b/.test(text))) found.year++;
    }
    if (!dateControl && (/gender|female|male|gioi tinh/.test(text) || (button && /\b(nam|nu)\b/.test(text)))) found.gender++;
  }
  const hasBirthday = found.birthday > 0 || (found.day > 0 && found.month > 0 && found.year > 0);
  found.stage = found.username && found.password && hasBirthday ? 'full' : found.username && !found.password && (hasContinue || hasBirthday) ? 'profile' : found.password && !found.username && hasAddPassword ? 'password' : 'unknown';
  return found;
})();
