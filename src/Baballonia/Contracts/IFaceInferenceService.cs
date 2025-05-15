using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Baballonia.Services.Inference.Enums;
using Baballonia.Services.Inference.Models;
using Baballonia.Services.Inference.Platforms;
using Microsoft.ML.OnnxRuntime;
using OpenCvSharp;

namespace Baballonia.Contracts;

public interface IFaceInferenceService : IInferenceService
{

}
