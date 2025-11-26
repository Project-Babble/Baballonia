using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Baballonia.Contracts;

namespace Baballonia.Services;

public class DataUploaderService
{
    private const string GarageEndpoint = "http://207.211.165.193:3900";
    private const string BucketName = "babbledata";
    private const string UploaderIdentifierOne = "GK1b5679b9dba9ff5b96e15cca";
    private const string UploaderIdentifierTwo = "4791149192ade8866bda2f09236e5f4cb0a5c76f10b7ec71cfd15e9995d360c0";
    private const string PublicKey = "";

    private readonly AmazonS3Client _uploaderClient;
    private readonly IIdentityService _identityService;
    private readonly RSA _publicRsa;

    public DataUploaderService(IIdentityService identityService)
    {
        _identityService = identityService;

        var uploaderConfig = new AmazonS3Config
        {
            ServiceURL = GarageEndpoint,
            ForcePathStyle = true,
            SignatureMethod = SigningAlgorithm.HmacSHA256,
            AuthenticationRegion = "garage"
        };

        var uploaderCredentials = new BasicAWSCredentials(
            UploaderIdentifierOne,
            UploaderIdentifierTwo
        );

        // Uncomment this once we have a public key
        // If this is empty this will crash
        // _publicRsa = LoadPublicKey(PublicKey);
        _uploaderClient = new AmazonS3Client(uploaderCredentials, uploaderConfig);
    }

    public async Task UploadDataAsync(string pathToFile)
    {
        var dataToUpload = await File.ReadAllBytesAsync(pathToFile);
        var fileName = Path.GetFileName(pathToFile);
        var uniqueName = $"{_identityService.GetUniqueUserId()}_{fileName}";

        // Encrypt the data before uploading
        var encryptedData = EncryptData(dataToUpload);

        var putRequest = new PutObjectRequest
        {
            BucketName = BucketName,
            Key = uniqueName,
            ChecksumSHA256 = ComputeSha256Hash(encryptedData),
            InputStream = new MemoryStream(encryptedData)
        };

        await _uploaderClient.PutObjectAsync(putRequest);
    }

    private byte[] EncryptData(byte[] data)
    {
        // For large files, use hybrid encryption: RSA for AES key, AES for data
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.GenerateKey();
        aes.GenerateIV();

        byte[] encryptedData;
        using (var encryptor = aes.CreateEncryptor())
        using (var msEncrypt = new MemoryStream())
        {
            using (var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
            {
                csEncrypt.Write(data, 0, data.Length);
            }
            encryptedData = msEncrypt.ToArray();
        }

        var encryptedAesKey = _publicRsa.Encrypt(aes.Key, RSAEncryptionPadding.OaepSHA256);

        // Package: [encrypted key length (4 bytes)][encrypted AES key][IV (16 bytes)][encrypted data]
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write(encryptedAesKey.Length);
        bw.Write(encryptedAesKey);
        bw.Write(aes.IV);
        bw.Write(encryptedData);

        return ms.ToArray();
    }

    private static RSA LoadPublicKey(string publicKeyPem)
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(publicKeyPem);
        return rsa;
    }

    private static string ComputeSha256Hash(byte[] rawData)
    {
        return Convert.ToHexString(SHA256.HashData(rawData));
    }
}
