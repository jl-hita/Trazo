using Trazo.Api.Support;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Trazo.Api.Contracts;
using Trazo.Api.Security;
using Trazo.Domain.Identity;
using Trazo.Infrastructure.Persistence;
using Trazo.Infrastructure.Settings;
namespace Trazo.Api.Endpoints;
public static class AccountEmailEndpoints
{
 public static IEndpointRouteBuilder MapAccountEmailEndpoints(this IEndpointRouteBuilder endpoints)
 {
  var group=endpoints.MapGroup("/api/v1/auth").WithTags("Account security").RequireRateLimiting("auth");
  group.MapPost("/email/verify",VerifyAsync);
  group.MapPost("/email/resend",ResendAsync);
  group.MapPost("/password/forgot",ForgotAsync);
  group.MapPost("/password/reset",ResetAsync);
  return endpoints;
 }
 private static async Task<IResult> VerifyAsync(EmailTokenRequest request,TrazoDbContext db,Trazo.Infrastructure.Growth.ISignupTrialService growth,CancellationToken ct)
 {
  var token=await FindToken(request.Token,"verify-email",db,ct);
  if(token is null)return ApiErrors.BadRequest(new{error="The verification link is invalid or expired."});
  var user=await db.Users.SingleOrDefaultAsync(x=>x.Id==token.UserId,ct);
  if(user is null)return ApiErrors.BadRequest(new{error="The verification link is invalid or expired."});
  var now=DateTimeOffset.UtcNow;
  await using var tx=await db.Database.BeginTransactionAsync(ct);
  var consumed=await db.EmailActionTokens.Where(x=>x.Id==token.Id&&x.ConsumedAt==null&&x.ExpiresAt>now)
   .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ConsumedAt,now),ct);
  if(consumed!=1)return ApiErrors.BadRequest(new{error="The verification link is invalid or expired."});
  user.EmailVerifiedAt=now;user.UpdatedAt=now;
  await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
  await growth.ActivatedAsync(user.Id,ct);
  return Results.Ok(new{verified=true});
 }
 private static async Task<IResult> ResendAsync(ResendVerificationRequest request,TrazoDbContext db,IEmailSender email,IRuntimeSettings config,DeletionRequestThrottle throttle,Trazo.Application.Localization.RequestLanguage language,CancellationToken ct)
 {
  var normalized=Normalize(request.Email);
  var user=normalized is null?null:await db.Users.SingleOrDefaultAsync(x=>x.NormalizedEmail==normalized,ct);
  // At most a few emails per address and hour, whoever asks: the per-IP limit alone lets one address be flooded.
  if(user is not null&&user.EmailVerifiedAt is null&&user.PasswordHash is not null&&throttle.TryAcquire("verify|"+normalized))
   await IssueAsync(user,"verify-email","Verifica tu correo de TRAZO","Verifica tu cuenta usando este enlace:",TimeSpan.FromHours(24),db,email,config,ct,language:language.Current);
  return Results.Accepted(value:new{message=Trazo.Application.Localization.Localizer.Translate("Si existe una cuenta pendiente de verificación, enviaremos un correo.",language.Current)});
 }
 private static async Task<IResult> ForgotAsync(ForgotPasswordRequest request,HttpRequest http,TrazoDbContext db,IEmailSender email,IRuntimeSettings config,DeletionRequestThrottle throttle,TimeProvider clock,ILoggerFactory loggers,Trazo.Application.Localization.RequestLanguage language,CancellationToken ct)
 {
  var normalized=Normalize(request.Email);
  var user=normalized is null?null:await db.Users.SingleOrDefaultAsync(x=>x.NormalizedEmail==normalized,ct);
  var origin=RequestOrigin.Classify(http.Headers.UserAgent.ToString());
  var outcome="no_account";
  try
  {
   if(user is null)outcome="no_account";
   else if(user.PasswordHash is null)outcome="no_password";
   else if(!throttle.TryAcquire("reset|"+normalized))outcome="throttled";
   else
   {
    outcome="send_failed";
    // The email says where and when the request came from, so a person who did not ask for it can tell what happened.
    await IssueAsync(user,"reset-password","Restablece tu contraseña de TRAZO","Usa este enlace para restablecer tu contraseña:",TimeSpan.FromMinutes(30),db,email,config,ct,
     new RequestNote(RequestOrigin.Spanish(origin),clock.GetUtcNow()),language.Current);
    outcome="sent";
   }
  }
  finally
  {
   // Every request leaves a trace, never with the address: where it came from, what was done and for which account (if any).
   loggers.CreateLogger("Trazo.Audit").LogInformation("Password reset requested: origin {Origin}, outcome {Outcome}, user {UserId}.",
    RequestOrigin.LogName(origin),outcome,user?.Id);
  }
  return Results.Accepted(value:new{message="Si existe una cuenta compatible, enviaremos instrucciones."});
 }
 private static async Task<IResult> ResetAsync(ResetPasswordRequest request,TrazoDbContext db,PasswordHasher<TrazoUser> hasher,CancellationToken ct)
 {
  if(request.NewPassword is not{Length:>=12 and <=128}||!request.NewPassword.Any(char.IsUpper)||!request.NewPassword.Any(char.IsLower)||!request.NewPassword.Any(char.IsDigit))
   return Results.ValidationProblem(new Dictionary<string,string[]>{{"newPassword",new[]{"Use al menos 12 caracteres, con mayúsculas, minúsculas y números."}}});
  var token=await FindToken(request.Token,"reset-password",db,ct);
  if(token is null)return ApiErrors.BadRequest(new{error="El enlace no es válido o ha caducado."});
  var user=await db.Users.SingleOrDefaultAsync(x=>x.Id==token.UserId,ct);
  if(user is null)return ApiErrors.BadRequest(new{error="El enlace no es válido o ha caducado."});
  var now=DateTimeOffset.UtcNow;
  await using var tx=await db.Database.BeginTransactionAsync(ct);
  var consumed=await db.EmailActionTokens.Where(x=>x.Id==token.Id&&x.ConsumedAt==null&&x.ExpiresAt>now)
   .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ConsumedAt,now),ct);
  if(consumed!=1)return ApiErrors.BadRequest(new{error="El enlace no es válido o ha caducado."});
  user.PasswordHash=hasher.HashPassword(user,request.NewPassword);
  // A valid password-setup/reset link proves control of the mailbox.
  user.EmailVerifiedAt ??= now;
  user.FailedLoginAttempts=0;user.LockoutEnd=null;user.UpdatedAt=now;
  await db.RefreshTokens.Where(x=>x.UserId==user.Id&&x.RevokedAt==null)
   .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.RevokedAt,now),ct);
  await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
  return Results.Ok(new{passwordReset=true});
 }
 public static async Task<EmailActionToken?> FindToken(string raw,string purpose,TrazoDbContext db,CancellationToken ct)
 {
  if(string.IsNullOrWhiteSpace(raw)||raw.Length>200)return null;
  var hash=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw)));
  var now=DateTimeOffset.UtcNow;
  return await db.EmailActionTokens.SingleOrDefaultAsync(x=>x.TokenHash==hash&&x.Purpose==purpose&&x.ConsumedAt==null&&x.ExpiresAt>now,ct);
 }
 public static async Task<string> CreateTokenAsync(TrazoUser user,string purpose,TimeSpan lifetime,TrazoDbContext db,CancellationToken ct)
 {
  var raw=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+','-').Replace('/','_');
  var now=DateTimeOffset.UtcNow;
  await db.EmailActionTokens.Where(x=>x.UserId==user.Id&&x.Purpose==purpose&&x.ConsumedAt==null)
   .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ConsumedAt,now),ct);
  db.EmailActionTokens.Add(new EmailActionToken{Id=Guid.NewGuid(),UserId=user.Id,TokenHash=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw))),Purpose=purpose,CreatedAt=now,ExpiresAt=now.Add(lifetime)});
  await db.SaveChangesAsync(ct);
  return raw;
 }
 /// <summary>Where and when the request that caused an email came from; the email states it.</summary>
 public sealed record RequestNote(string From,DateTimeOffset At);
 public static async Task IssueAsync(TrazoUser user,string purpose,string subject,string intro,TimeSpan lifetime,TrazoDbContext db,IEmailSender email,IRuntimeSettings config,CancellationToken ct,RequestNote? requestNote=null,Trazo.Application.Localization.AppLanguage language=Trazo.Application.Localization.AppLanguage.Es)
 {
  language=Trazo.Application.Localization.LanguageNegotiation.FromChoice(user.Language)??language; // the language the user chose, else the request's
  string Tr(string text)=>Trazo.Application.Localization.Localizer.Translate(text,language)!;
  var raw=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+','-').Replace('/','_');
  var now=DateTimeOffset.UtcNow;
  await db.EmailActionTokens.Where(x=>x.UserId==user.Id&&x.Purpose==purpose&&x.ConsumedAt==null)
   .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ConsumedAt,now),ct);
  db.EmailActionTokens.Add(new EmailActionToken{Id=Guid.NewGuid(),UserId=user.Id,TokenHash=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw))),Purpose=purpose,CreatedAt=now,ExpiresAt=now.Add(lifetime)});
  await db.SaveChangesAsync(ct);
  var baseUrl=(await config.GetEmailAsync(ct)).ActionBaseUrl.TrimEnd('/');
  if(string.IsNullOrWhiteSpace(baseUrl))throw new InvalidOperationException("Email:ActionBaseUrl is required.");
  var url=$"{baseUrl}/account/{(purpose=="verify-email"?"verify-email":"reset-password")}?token={Uri.EscapeDataString(raw)}";
  var note=requestNote is null?"":Tr($"Esta solicitud se hizo {requestNote.From} el {requestNote.At.UtcDateTime:dd/MM/yyyy} a las {requestNote.At.UtcDateTime:HH:mm} (hora UTC).")+"\n\n";
  await email.SendAsync(user.Email,Tr(subject),$"{Tr(intro)}\n\n{url}\n\n{note}{Tr($"El enlace caduca en {(int)lifetime.TotalMinutes} minutos.")} {Tr("Si no has solicitado esta acción, ignora este mensaje.")}",ct);
 }
 private static string? Normalize(string? email)=>string.IsNullOrWhiteSpace(email)?null:email.Trim().ToUpperInvariant();
}
