// Formulario de la lista de espera. Envía el correo a la API de TRAZO.
(function () {
  var form = document.getElementById('wait-form');
  if (!form) return;
  var msg = document.getElementById('msg');
  var btn = form.querySelector('button');
  var t = JSON.parse(form.getAttribute('data-i18n'));
  form.addEventListener('submit', function (e) {
    e.preventDefault();
    msg.className = ''; msg.textContent = '';
    if (form.elements.company.value) return; // campo trampa para bots
    btn.disabled = true;
    fetch(form.getAttribute('data-endpoint'), {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        email: form.elements.email.value.trim(),
        language: document.documentElement.lang,
        consent: form.elements.consent.checked
      })
    }).then(function (r) {
      if (r.ok) { form.reset(); msg.className = 'ok'; msg.textContent = t.ok; }
      else if (r.status === 400 || r.status === 422) { msg.className = 'err'; msg.textContent = t.invalid; }
      else if (r.status === 429) { msg.className = 'err'; msg.textContent = t.later; }
      else { throw new Error(r.status); }
    }).catch(function () {
      msg.className = 'err'; msg.textContent = t.fail;
    }).then(function () { btn.disabled = false; });
  });
})();
