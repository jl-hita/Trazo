using System.Net;
using System.Net.Mail;
namespace Trazo.Api.Security;
public sealed class SmtpEmailSender(IConfiguration configuration) : IEmailSender
{
 public async Task SendAsync(string recipient,string subject,string body,CancellationToken cancellationToken)
 {
  var host=configuration["Email:Smtp:Host"];
  var from=configuration["Email:From"];
  if(string.IsNullOrWhiteSpace(host)||string.IsNullOrWhiteSpace(from))
   throw new InvalidOperationException("Transactional email is not configured.");
  var port=configuration.GetValue<int?>("Email:Smtp:Port")??587;
  using var message=new MailMessage(from,recipient,subject,body){IsBodyHtml=false};
  using var client=new SmtpClient(host,port){EnableSsl=configuration.GetValue<bool?>("Email:Smtp:EnableSsl")??true};
  var username=configuration["Email:Smtp:Username"];
  var password=configuration["Email:Smtp:Password"];
  if(!string.IsNullOrWhiteSpace(username))client.Credentials=new NetworkCredential(username,password);
  await client.SendMailAsync(message,cancellationToken);
 }
}
