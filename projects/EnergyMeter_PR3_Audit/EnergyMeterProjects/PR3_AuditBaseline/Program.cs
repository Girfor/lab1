var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// УЧЕБНАЯ УЯЗВИМАЯ КОНФИГУРАЦИЯ: только локальный стенд!
app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();
app.Run();

public partial class Program { }
