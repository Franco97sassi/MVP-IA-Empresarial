using LocalMind.Api.Data;
using LocalMind.Api.DTOs;
using LocalMind.Api.Models;
using LocalMind.Api.Services.Auth;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LocalMind.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IJwtService _jwtService;

    public AuthController(AppDbContext context, IJwtService jwtService)
    {
        _context = context;
        _jwtService = jwtService;
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request)
    {
        var emailExists = await _context.Users
            .AnyAsync(x => x.Email == request.Email);

        if (emailExists)
            return BadRequest("El email ya está registrado.");

        var user = new User
        {
            Email = request.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password)
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var token = _jwtService.GenerateToken(user);

        return Ok(new AuthResponse
        {
            Token = token,
            Email = user.Email
        });
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(x => x.Email == request.Email);

        if (user is null)
            return Unauthorized("Credenciales inválidas.");

        var validPassword = BCrypt.Net.BCrypt.Verify(
            request.Password,
            user.PasswordHash
        );

        if (!validPassword)
            return Unauthorized("Credenciales inválidas.");

        var token = _jwtService.GenerateToken(user);

        return Ok(new AuthResponse
        {
            Token = token,
            Email = user.Email
        });
    }


    [HttpPost("refresh")]
    public async Task<ActionResult<AuthTokensResponse>> Refresh(RefreshTokenRequest request)
    {
        var stored = await _context.RefreshTokens
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Token == request.RefreshToken);

        if (stored is null || stored.ExpiresAt < DateTime.UtcNow || stored.RevokedAt != null)
            return Unauthorized("Refresh token inválido o expirado.");

        // Rotación
        stored.RevokedAt = DateTime.UtcNow;
        var newRefresh = RefreshTokenGenerator.Generate();

        _context.RefreshTokens.Add(new RefreshToken
        {
            UserId = stored.UserId,
            Token = newRefresh,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        });

        var access = _jwtService.GenerateToken(stored.User);
        await _context.SaveChangesAsync();

        return Ok(new AuthTokensResponse
        {
            AccessToken = access,
            RefreshToken = newRefresh
        });
    }
    [HttpPost("revoke")]
    public async Task<IActionResult> Revoke(RefreshTokenRequest request)
    {
        var stored = await _context.RefreshTokens.FirstOrDefaultAsync(r => r.Token == request.RefreshToken);
        if (stored is null) return NotFound();

        stored.RevokedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return NoContent();
    }

}