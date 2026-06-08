using System.Security.Cryptography;
using System.Text;

namespace EspiaDesk.Core;

public class SessionCrypto
{
    private readonly RSA _rsa = RSA.Create(2048);
    private byte[]? _key;

    public byte[] GetPublicKeyBytes() => _rsa.ExportRSAPublicKey();

    /// <summary>Servidor: recebe chave pública do cliente, gera chave AES-256,
    /// encripta com a chave do cliente e devolve para envio.</summary>
    public byte[] EstablishAsServer(byte[] clientPublicKeyBytes)
    {
        _key = RandomNumberGenerator.GetBytes(32);
        using var clientRsa = RSA.Create();
        clientRsa.ImportRSAPublicKey(clientPublicKeyBytes, out _);
        return clientRsa.Encrypt(_key, RSAEncryptionPadding.OaepSHA256);
    }

    /// <summary>Cliente: decripta chave de sessão com chave privada própria.</summary>
    public void EstablishAsClient(byte[] encryptedKey)
        => _key = _rsa.Decrypt(encryptedKey, RSAEncryptionPadding.OaepSHA256);

    /// <summary>Encripta payload — IV aleatório é prefixado ao ciphertext.</summary>
    public byte[] Encrypt(byte[] data)
    {
        if (_key is null) return data;
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();
        using var enc = aes.CreateEncryptor();
        var cipher = enc.TransformFinalBlock(data, 0, data.Length);
        var result = new byte[16 + cipher.Length];
        aes.IV.CopyTo(result, 0);
        cipher.CopyTo(result, 16);
        return result;
    }

    /// <summary>Decripta payload — extrai IV dos primeiros 16 bytes.</summary>
    public byte[] Decrypt(byte[] data)
    {
        if (_key is null) return data;
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.IV = data[..16];
        using var dec = aes.CreateDecryptor();
        return dec.TransformFinalBlock(data, 16, data.Length - 16);
    }

    public bool IsReady => _key is not null;

    public static string HashPassword(string password)
    {
        var bytes = Encoding.UTF8.GetBytes("EspiaDesk_Salt_2024" + password);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLower();
    }

    public static bool VerifyPassword(string password, string hash)
        => HashPassword(password) == hash;

    public static string GenerateSessionId()
    {
        var suffix = Random.Shared.Next(0, 1_000_000).ToString("D6");
        return $"ED-{suffix}";
    }
}
