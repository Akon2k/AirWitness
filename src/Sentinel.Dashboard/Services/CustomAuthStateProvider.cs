using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using System.Security.Claims;

namespace Sentinel.Dashboard.Services;

public class CustomAuthStateProvider : AuthenticationStateProvider
{
    private readonly ProtectedSessionStorage _sessionStorage;
    private readonly ClaimsPrincipal _anonymous = new(new ClaimsIdentity());
    private bool _isAuthenticatedInMemory = false; // Caché de sesión para evitar loops de túnel

    public CustomAuthStateProvider(ProtectedSessionStorage sessionStorage)
    {
        _sessionStorage = sessionStorage;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        // 1. Prioridad: Caché en memoria (instantáneo, no requiere JS)
        if (_isAuthenticatedInMemory)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, "Admin") };
            var identity = new ClaimsIdentity(claims, "CustomAuth");
            return new AuthenticationState(new ClaimsPrincipal(identity));
        }

        try
        {
            var sessionTask = _sessionStorage.GetAsync<bool>("IsAuthenticated");
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(1)); // Reducido para mayor agilidad en túneles

            var completedTask = await Task.WhenAny(sessionTask.AsTask(), timeoutTask);

            if (completedTask == timeoutTask)
            {
                System.Console.WriteLine("[AUTH] ⚠ Timeout de seguridad alcanzado. Accediendo como invitado para evitar cuelgue.");
                return new AuthenticationState(_anonymous);
            }

            var userSessionResult = await sessionTask;
            var isAuthenticated = userSessionResult.Success ? userSessionResult.Value : false;

            if (isAuthenticated)
            {
                _isAuthenticatedInMemory = true; // Sincronizar el caché para futuras llamadas
                var claims = new List<Claim> { new Claim(ClaimTypes.Name, "Admin") };
                var identity = new ClaimsIdentity(claims, "CustomAuth");
                var user = new ClaimsPrincipal(identity);
                return new AuthenticationState(user);
            }
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"[AUTH-PROVIDER] Error recuperando sesión: {ex.Message}");
        }

        return new AuthenticationState(_anonymous);
    }

    public async Task MarkUserAsAuthenticated()
    {
        _isAuthenticatedInMemory = true; // Setear caché ANTES del SetAsync para disponibilidad inmediata
        try {
            await _sessionStorage.SetAsync("IsAuthenticated", true);
        } catch { } 
        
        var claims = new List<Claim> { new Claim(ClaimTypes.Name, "Admin") };
        var identity = new ClaimsIdentity(claims, "CustomAuth");
        var user = new ClaimsPrincipal(identity);

        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(user)));
    }

    public async Task MarkUserAsLoggedOut()
    {
        await _sessionStorage.DeleteAsync("IsAuthenticated");
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(_anonymous)));
    }
}
