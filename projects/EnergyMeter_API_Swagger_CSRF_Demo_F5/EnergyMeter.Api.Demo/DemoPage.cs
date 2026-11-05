internal static class DemoPage
{
    public const string Html = """
<!doctype html>
<html lang="ru">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>EnergyMeter API — Демонстрация</title>
  <style>
    :root { color-scheme: dark; font-family: Segoe UI, Arial, sans-serif; background:#0b101b; color:#e9f0ff }
    * { box-sizing: border-box }
    body { margin:0; background:radial-gradient(ellipse at top right,#1a3257 0%,#0b101b 54%); min-height:100vh }
    .wrap { max-width:1040px; margin:0 auto; padding:50px 24px 80px }
    .eyebrow { color:#88b9fa; letter-spacing:.2em; text-transform:uppercase; font-size:12px; font-weight:700 }
    h1 { font-size:clamp(32px,5vw,54px); line-height:1.1; margin:12px 0 10px }
    .subtitle { color:#a8bad5; font-size:17px; max-width:670px; line-height:1.6 }
    .panel { background:#151e2f; border:1px solid #32415e; border-radius:18px; padding:25px; margin-top:26px; box-shadow:0 14px 34px #0004 }
    .bar { display:flex; align-items:center; flex-wrap:wrap; justify-content:space-between; gap:16px }
    .count { color:#a8bad5; font-weight:600 }
    .badge { border:1px solid #5b738e; background:#24364b; color:#d7e6fa; padding:9px 16px; border-radius:100px; font-weight:700 }
    .badge.passed { background:#113d35; border-color:#2b8b70; color:#6cf1c1 }
    .badge.failed { background:#481f2c; border-color:#b14366; color:#ff9baa }
    .grid { display:grid; grid-template-columns:repeat(auto-fill,minmax(290px,1fr)); gap:12px; margin-top:23px }
    .test { display:flex; justify-content:space-between; gap:16px; border:1px solid #30405a; background:#111a2b; border-radius:12px; padding:13px 15px }
    .test-name { font-size:14px; line-height:1.4 }
    .status { color:#5ef1b9; font-weight:800; white-space:nowrap; font-size:13px }
    .status.fail { color:#ff90a7 }
    h2 { font-size:18px; margin:0 0 12px }
    .links { display:flex; flex-wrap:wrap; gap:12px }
    a { display:inline-block; text-decoration:none; color:#b3d4ff; border:1px solid #42638d; border-radius:10px; padding:10px 15px; background:#1a2a41 }
    a:hover { color:white; background:#254266 }
    code { color:#a8e0e6; overflow-wrap:anywhere }
    .small { margin-top:16px; line-height:1.6; color:#97a9c6; font-size:13px }
    #report { overflow-wrap:anywhere; margin-top:15px; font-size:13px; color:#a8bad5 }
  </style>
</head>
<body>
  <main class="wrap">
    <div class="eyebrow">Вариант 15 / ASP.NET Core 8</div>
    <h1>EnergyMeter API</h1>
    <p class="subtitle">Демонстрация REST API v1/v2, аутентификации, идемпотентности, Swagger и защиты от CSRF. Все запросы выполняются автоматически на вашем компьютере.</p>
    <section class="panel">
      <div class="bar">
        <div>
          <h2>Результаты автоматической проверки</h2>
          <div id="count" class="count">Ожидание первых результатов...</div>
        </div>
        <div id="summary" class="badge">Выполняется</div>
      </div>
      <div id="tests" class="grid" aria-live="polite"></div>
      <div id="report"></div>
    </section>
    <section class="panel">
      <h2>Маршруты для демонстрации</h2>
      <div class="links">
        <a target="_blank" rel="noopener" href="/api/v1/readings">Просмотреть API v1</a>
        <a target="_blank" rel="noopener" href="/api/v2/readings">Просмотреть API v2</a>
        <a target="_blank" rel="noopener" href="/swagger/index.html">Открыть Swagger UI</a>
      </div>
      <div class="small">Swagger доступен только в Development. Запись показаний требует API-ключа. Административная операция защищена cookie-авторизацией и CSRF-токеном. В рабочей среде обязательно используйте HTTPS и полноценную систему учётных записей.</div>
    </section>
  </main>
  <script>
    let finished = false;
    async function refresh() {
      if (finished) return;
      try {
        const response = await fetch('/demo/status', {cache: 'no-store'});
        const data = await response.json();
        const entries = data.checks || [];
        document.getElementById('count').textContent = 'Проверок выполнено: ' + entries.length + ' из 18';
        const grid = document.getElementById('tests');
        grid.replaceChildren();
        for (const item of entries) {
          const el = document.createElement('div');
          el.className = 'test';
          const name = document.createElement('span');
          name.className = 'test-name';
          name.textContent = item.name;
          name.title = item.details;
          const status = document.createElement('span');
          status.className = 'status' + (item.status === 'PASS' ? '' : ' fail');
          status.textContent = item.status;
          el.append(name, status);
          grid.appendChild(el);
        }
        if (data.done) {
          finished = true;
          const status = document.getElementById('summary');
          status.textContent = data.allPassed ? 'ИТОГ: PASS' : 'ИТОГ: FAIL';
          status.className = 'badge ' + (data.allPassed ? 'passed' : 'failed');
          document.getElementById('report').textContent = 'Текстовый отчёт: ' + data.reportPath;
        }
      } catch (e) {
        document.getElementById('count').textContent = 'Ожидание запуска локального API...';
      }
    }
    refresh();
    setInterval(refresh, 900);
  </script>
</body>
</html>
""";
}
