using System.Security.Cryptography;
using LabInventario.Data;

namespace LabInventario.Services
{
    /// <summary>
    /// Maneja la contraseña de administrador: se guarda con hash + salt
    /// (PBKDF2/SHA256, nunca en texto plano) en la tabla `configuracion`.
    /// El rol "Usuario" no requiere contraseña: cualquiera puede entrar a
    /// registrar salidas/entradas, que es justo lo que se quiere permitir
    /// sin fricción. Solo entrar como Administrador exige verificarla.
    /// </summary>
    public class AuthService
    {
        private const string ClaveHash = "AdminPasswordHash";
        private const string ClaveSalt = "AdminPasswordSalt";
        private const string PasswordPorDefecto = "admin123";
        private const int Iteraciones = 100_000;

        private readonly ConfiguracionRepository _config = new();

        public AuthService()
        {
            // Si es la primera vez que corre la aplicación, siembra la
            // contraseña por defecto para que siempre haya un administrador
            // capaz de entrar.
            if (_config.Obtener(ClaveHash) is null)
                EstablecerPasswordAdmin(PasswordPorDefecto);
        }

        public bool UsaPasswordPorDefecto() => ValidarPasswordAdmin(PasswordPorDefecto);

        public bool ValidarPasswordAdmin(string password)
        {
            var saltTexto = _config.Obtener(ClaveSalt);
            var hashTexto = _config.Obtener(ClaveHash);
            if (saltTexto is null || hashTexto is null) return false;

            var salt = Convert.FromBase64String(saltTexto);
            var hashEsperado = Convert.FromBase64String(hashTexto);
            var hashIngresado = CalcularHash(password, salt);

            return CryptographicOperations.FixedTimeEquals(hashEsperado, hashIngresado);
        }

        public void EstablecerPasswordAdmin(string nuevaPassword)
        {
            var salt = RandomNumberGenerator.GetBytes(16);
            var hash = CalcularHash(nuevaPassword, salt);
            _config.Establecer(ClaveSalt, Convert.ToBase64String(salt));
            _config.Establecer(ClaveHash, Convert.ToBase64String(hash));
        }

        private static byte[] CalcularHash(string password, byte[] salt) =>
            Rfc2898DeriveBytes.Pbkdf2(password, salt, Iteraciones, HashAlgorithmName.SHA256, 32);
    }
}
