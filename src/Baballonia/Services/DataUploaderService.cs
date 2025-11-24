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

    private readonly AmazonS3Client _uploaderClient;
    private readonly IIdentityService _identityService;

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

        _uploaderClient = new AmazonS3Client(uploaderCredentials, uploaderConfig);
    }

    public async Task UploadDataAsync(string pathToFile)
    {
        var dataToUpload = await File.ReadAllBytesAsync(pathToFile);

        var fileName = Path.GetFileName(pathToFile);
        var uniqueName = $"{_identityService.GetUniqueUserId()}_{fileName}";
        var putRequest = new PutObjectRequest
        {
            BucketName = BucketName,
            Key = uniqueName,
            ChecksumSHA256 = ComputeSha256Hash(dataToUpload),
            InputStream = new MemoryStream(dataToUpload)
        };

        await _uploaderClient.PutObjectAsync(putRequest);
    }

    private static string ComputeSha256Hash(byte[] rawData)
    {
        return Convert.ToHexString(SHA256.HashData(rawData)); // .ToLowerInvariant();
    }
}
