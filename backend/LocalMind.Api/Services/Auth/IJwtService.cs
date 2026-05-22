 
using LocalMind.Api.Models;
 

namespace LocalMind.Api.Services.Auth;

public interface IJwtService
{
    string GenerateToken(User user);
}