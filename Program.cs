using System.Net;
using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using Twilio;
using Twilio.Rest.Api.V2010.Account;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}


app.MapPost("/api/data", async (RequestWrapper wrapper, AppDbContext db, IConfiguration config) =>
{
    try
    {
        var data = wrapper.contacto;


        var entity = new DataEntity
        {
            Uuid = data.uuid,
            Nombre = data.nombre,
            Correo = data.correo,
            Telefono = data.telefono,
            FechaCreacion = DateTime.UtcNow
        };

        db.DataEntities.Add(entity);
        await db.SaveChangesAsync();


        string serverIp = Dns.GetHostEntry(Dns.GetHostName())
            .AddressList
            .FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            ?.ToString() ?? "Unknown";


        await SendEmail(data.correo, data.nombre, data.uuid, serverIp, config);

        await SendSms(data.telefono, data.nombre, data.uuid, serverIp, config);

        return Results.Ok(new
        {
            message = "Datos procesados exitosamente",
            uuid = data.uuid,
            serverIp = serverIp
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error: {ex.Message}");
    }
});

app.Run();

async Task SendEmail(string toEmail, string nombre, string uuid, string serverIp, IConfiguration config)
{
    var smtpClient = new SmtpClient(config["Email:SmtpHost"])
    {
        Port = int.Parse(config["Email:SmtpPort"]),
        Credentials = new NetworkCredential(
            config["Email:Username"],
            config["Email:Password"]
        ),
        EnableSsl = true
    };

    var mailMessage = new MailMessage
    {
        From = new MailAddress(config["Email:FromAddress"]),
        Subject = "Notificación de Registro",
        Body = $"Nombre: {nombre}\nUUID: {uuid}\nIP: {serverIp}",
        IsBodyHtml = false
    };

    mailMessage.To.Add(toEmail);

    await smtpClient.SendMailAsync(mailMessage);
}

async Task SendSms(string phoneNumber, string nombre, string uuid, string serverIp, IConfiguration config)
{
    string accountSid = config["Twilio:AccountSid"];
    string authToken = config["Twilio:AuthToken"];
    string fromPhone = config["Twilio:FromPhone"];

    TwilioClient.Init(accountSid, authToken);

    var message = await MessageResource.CreateAsync(
        body: $"Nombre: {nombre}\nUUID: {uuid}\nIP: {serverIp}",
        from: new Twilio.Types.PhoneNumber(fromPhone),
        to: new Twilio.Types.PhoneNumber(phoneNumber)
    );
}

public record RequestWrapper(ContactoData contacto);
public record ContactoData(string uuid, string nombre, string correo, string telefono);

public class DataEntity
{
    public int Id { get; set; }
    public string Uuid { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Correo { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public DateTime FechaCreacion { get; set; }
}

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<DataEntity> DataEntities { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DataEntity>(entity =>
        {
            entity.ToTable("registros"); 
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Uuid).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Nombre).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Correo).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Telefono).IsRequired().HasMaxLength(20);
        });
    }
}