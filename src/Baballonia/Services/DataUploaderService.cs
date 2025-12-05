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
    private const string PublicKey = @"-----BEGIN PUBLIC KEY-----
MIICIjANBgkqhkiG9w0BAQEFAAOCAg8AMIICCgKCAgEAvaow3ZwmKDl6x5Xi7o8o
Q+bQ7OfgAS5GyJbgx9Tj2px04jZObbCoLnyGkbVRptCMIqrP/5X+Lpc3npcSKxa3
6EOaINvzElA2wFbHc+Ok67fLqy4VqR/0si1Sivx3miiZfzFz66WlREeg5HZS0aA8
WsyaeZtdiIcLzKriOuj2Q3a2p6hxbsij45OAvymp2qLpJB7/No0CHcKiwR2mEL8j
0s4JCHy+AuHZqKMg0fTJ6dGNjljkg3FR4DBv4rJrrLrMEGniPkoc0tWttr5vCkjs
nAoQ6dMoYqxjumEt9C4lXB7fIipLiha4N5z7gm6AIFycdlV0cQZsBi7LyVqCqQhP
QTkFEMksYQswS5+g7jGeWAJXYmiSdgJIf/9iiec5LyLv4RUnDTxDa9Z1i7jv1fAC
0Fiibo4Fk2maHs5N3Oho2wytYLzcd8KTwEFORhkylktZ5vXXwMtyXns4EDEenbSK
yMd7YnLo/m03bkPFlfaB6Tb76B7/in1qkve/JOJM0n9Q9QcJ/befkfopvQvcSg6l
yBUvCjd82gpyrz9/t33dKxUdceBXQ0w1HLlHwI3VL4tGYA4xt5dsxD9pegBPeblI
cGmQGUQ2S2mcUlUC5lN2EMkuJybKiMeALJNEk2IqBi/rZIWrCzHTcuuvjSOjyck0
8OvTbH1s4f6TwR1LHqZG65UCAwEAAQ==
-----END PUBLIC KEY-----
";

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

        _publicRsa = RSA.Create();
        _publicRsa.ImportFromPem(PublicKey);

        _uploaderClient = new AmazonS3Client(uploaderCredentials, uploaderConfig);
    }

    public async Task UploadDataAsync(string pathToFile)
    {
        var dataToUpload = await File.ReadAllBytesAsync(pathToFile);
        var fileName = Path.GetFileName(pathToFile);
        var uniqueName = $"{_identityService.GetUniqueUserId()}_{fileName}";

        var encryptedData = _publicRsa.Encrypt(dataToUpload, RSAEncryptionPadding.Pkcs1);

        var putRequest = new PutObjectRequest
        {
            BucketName = BucketName,
            Key = uniqueName,
            ChecksumSHA256 = Convert.ToHexString(SHA256.HashData(encryptedData)),
            InputStream = new MemoryStream(encryptedData)
        };

        await _uploaderClient.PutObjectAsync(putRequest);
    }
}
