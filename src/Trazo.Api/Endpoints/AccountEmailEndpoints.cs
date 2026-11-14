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
 private static async Task<IResult> VerifyAsync(EmailTokenRequest request,TrazoDbContext db,CancellationToken ct)
 {
  var token=await FindToken(request.Token,"verify-email",db,ct);
  if(token is null)return Results.BadRequest(new{error="The verification link is invalid or expired."});
  var user=await db.Users.SingleOrDefaultAsync(x=>x.Id==token.UserId,ct);
  if(user is null)return Results.BadRequest(new{error="The verification link is invalid or expired."});
  var now=DateTimeOffset.UtcNow;
  await using var tx=await db.Database.BeginTransactionAsync(ct);
  var consumed=await db.EmailActionTokens.Where(x=>x.Id==token.Id&&x.ConsumedAt==null&&x.ExpiresAt>now)
   .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ConsumedAt,now),ct);
  if(consumed!=1)return Results.BadRequest(new{error="The verification link is invalid or expired."});
  user.EmailVerifiedAt=now;user.UpdatedAt=now;
  await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
  return Results.Ok(new{verified=true});
 }
 private static async Task<IResult> ResendAsync(ResendVerificationRequest request,TrazoDbContext db,IEmailSender email,IRuntimeSettings config,CancellationToken ct)
 {
  var normalized=Normalize(request.Email);
  var user=normalized is null?null:await db.Users.SingleOrDefaultAsync(x=>x.NormalizedEmail==normalized,ct);
  if(user is not null&&user.EmailVerifiedAt is null&&user.PasswordHash is not null)
   await IssueAsync(user,"verify-email","Verifica tu correo de TRAZO","Verifica tu cuenta usando este enlace:",TimeSpan.FromHours(24),db,email,config,ct);
  return Results.Accepted(value:new{message="Si existe una cuenta pendiente de verificación, enviaremos un correo."});
 }
 private static async Task<IResult> ForgotAsync(ForgotPasswordRequest request,TrazoDbContext db,IEmailSender email,IRuntimeSettings config,CancellationToken ct)
 {
  var normalized=Normalize(request.Email);
  var user=normalized is null?null:await db.Users.SingleOrDefaultAsync(x=>x.NormalizedEmail==normalized,ct);
  if(user is not null&&user.PasswordHash is not null)
   await IssueAsync(user,"reset-password","Restablece tu contraseña de TRAZO","Usa este enlace para restablecer tu contraseña:",TimeSpan.FromMinutes(30),db,email,config,ct);
  return Results.Accepted(value:new{message="Si existe una cuenta compatible, enviaremos instrucciones."});
 }
 private static async Task<IResult> ResetAsync(ResetPasswordRequest request,TrazoDbContext db,PasswordHasher<TrazoUser> hasher,CancellationToken ct)
 {
  if(request.NewPassword is not{Length:>=12 and <=128}||!request.NewPassword.Any(char.IsUpper)||!request.NewPassword.Any(char.IsLower)||!request.NewPassword.Any(char.IsDigit))
   return Results.ValidationProblem(new Dictionary<string,string[]>{{"newPassword",new[]{"Use al menos 12 caracteres, con mayúsculas, minúsculas y números."}}});
  var token=await FindToken(request.Token,"reset-password",db,ct);
  if(token is null)return Results.BadRequest(new{error="El enlace no es válido o ha caducado."});
  var user=await db.Users.SingleOrDefaultAsync(x=>x.Id==token.UserId,ct);
  if(user is null)return Results.BadRequest(new{error="El enlace no es válido o ha caducado."});
  var now=DateTimeOffset.UtcNow;
  await using var tx=await db.Database.BeginTransactionAsync(ct);
  var consumed=await db.EmailActionTokens.Where(x=>x.Id==token.Id&&x.ConsumedAt==null&&x.ExpiresAt>now)
   .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ConsumedAt,now),ct);
  if(consumed!=1)return Results.BadRequest(new{error="El enlace no es válido o ha caducado."});
  user.PasswordHash=hasher.HashPassword(user,request.NewPassword);
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
 public static async Task IssueAsync(TrazoUser user,string purpose,string subject,string intro,TimeSpan lifetime,TrazoDbContext db,IEmailSender email,IRuntimeSettings config,CancellationToken ct)
 {
  var raw=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+','-').Replace('/','_');
  var now=DateTimeOffset.UtcNow;
  await db.EmailActionTokens.Where(x=>x.UserId==user.Id&&x.Purpose==purpose&&x.ConsumedAt==null)
   .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ConsumedAt,now),ct);
  db.EmailActionTokens.Add(new EmailActionToken{Id=Guid.NewGuid(),UserId=user.Id,TokenHash=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw))),Purpose=purpose,CreatedAt=now,ExpiresAt=now.Add(lifetime)});
  await db.SaveChangesAsync(ct);
  var baseUrl=(await config.GetEmailAsync(ct)).ActionBaseUrl.TrimEnd('/');
  if(string.IsNullOrWhiteSpace(baseUrl))throw new InvalidOperationException("Email:ActionBaseUrl is required.");
  var url=$"{baseUrl}/account/{(purpose=="verify-email"?"verify-email":"reset-password")}?token={Uri.EscapeDataString(raw)}";
  await email.SendAsync(user.Email,subject,$"{intro}\n\n{url}\n\nEl enlace caduca en {(int)lifetime.TotalMinutes} minutos. Si no has solicitado esta acción, ignora este mensaje.",ct);
 }
 private static string? Normalize(string? email)=>string.IsNullOrWhiteSpace(email)?null:email.Trim().ToUpperInvariant();
}
