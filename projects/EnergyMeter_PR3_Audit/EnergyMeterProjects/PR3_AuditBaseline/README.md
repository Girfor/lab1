# ПР3 — исходный фрагмент для аудита

Демонстрационный проект ASP.NET Core Web API, специально содержащий недостатки:
- Swagger доступен независимо от среды;
- у POST нет авторизации и CSRF-проверки;
- метод `addReading` нарушает соглашения об именовании;
- отсутствует верхняя граница значения и проверка счетчика;
- контроллер хранит данные сам, вместо использования сервиса.

**Использовать только на localhost и только для учебного аудита. Не публиковать в интернет.**

Запуск: `dotnet run --project EnergyMeter.PR3.Audit.csproj`.
Открыть `http://localhost:5203/swagger`.

Пример (PowerShell):
```powershell
Invoke-RestMethod -Method Post "http://localhost:5203/api/readings?ID=10&value=12.5"
```
