using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Baballonia.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using VRCFaceTracking;
using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Core.Models;
using VRCFaceTracking.Core.OSC.DataTypes;
using VRCFaceTracking.Core.Params;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.DataTypes;
using VRCFaceTracking.Core.Types;

namespace Baballonia.Services;

/// <summary>
/// Initializes VRCFaceTracking loading libraries from Utils.VrcftLibsDirectory and invokes an event
/// whenever updated tracking data is available.
/// </summary>
public class VRCFaceTrackingService : BackgroundService
{
    /// <summary>
    /// This event is invoked whenever updated face tracking data is available with the OSC path (or parameter name)
    /// and the parameters current value as a float.
    /// </summary>
    public Action<string, float> OnParameterUpdated = (parameterPath, value) => { };

    private MainIntegrated _standalone;
    private List<ICustomFaceExpression> _customFaceExpressions = new();
    private Contracts.ILocalSettingsService  _localSettingsService;
    private static readonly HttpClient Client = new();
    private readonly ParameterSenderService _parameterSenderService;
    private string _lastAvatarId;

    public VRCFaceTrackingService(Contracts.ILocalSettingsService localSettingsService, Contracts.IDispatcherService dispatcherService, ParameterSenderService parameterSenderService)
    {
        _localSettingsService = localSettingsService;
        _parameterSenderService = parameterSenderService;
        if (!localSettingsService.ReadSetting<bool>("VRC_UseVRCFaceTracking")) return;
        ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
        {
            builder
                .AddConsole()
                .SetMinimumLevel(LogLevel.Trace);
        });
        UnifiedTrackingMutator mutator = new UnifiedTrackingMutator(
            loggerFactory.CreateLogger<UnifiedTrackingMutator>(), new VRCFaceTrackingSettings(localSettingsService));
        UnifiedLibManager unifiedLibManager = new UnifiedLibManager(loggerFactory,
            new VRCFaceTrackingDispatcher(dispatcherService), new VRCFaceTrackingModuleData());
        _standalone = new MainIntegrated(loggerFactory, unifiedLibManager, mutator);
    }

    /// <summary>
    /// Updates all VRCFaceTracking Parameters to only include the parameters relative to your current avatar
    /// </summary>
    /// <param name="parameters">The parameters your avatar contains</param>
    public void OnNewAvatarLoaded(IParameterDefinition[] parameters)
    {
        _customFaceExpressions.RemoveAll(x => x.GetType() == VRCFTParameters.ParameterType);
        _customFaceExpressions.AddRange(VRCFTParameters.UpdateParameters(parameters));

        Dictionary<string, List<string>> expressionMap = [];
        foreach (var customFaceExpression in _customFaceExpressions)
        {
            string lastPart = customFaceExpression.Name.Split('/', StringSplitOptions.RemoveEmptyEntries).Last();
            List<string>? existing;
            if (expressionMap.TryGetValue(lastPart, out existing))
                existing.Add(customFaceExpression.Name);
            else
                expressionMap.Add(lastPart, [customFaceExpression.Name]);
        }

        Dictionary<string, string[]> expressionArrayMap = [];
        foreach (var expression in expressionMap)
            expressionArrayMap.Add(expression.Key, expression.Value.ToArray());
        _parameterSenderService.SetNativeVrchatConvertedDict(expressionArrayMap);
    }

    internal async void PullParametersFromOSCAddress(string ip, int port)
    {
        string url = $"http://{ip}:{port}/";
        string jsonContent = await Client.GetStringAsync(url);
        OscAvatarParameters? root = JsonConvert.DeserializeObject<OscAvatarParameters>(jsonContent);
        if (root == null) return;
        var avatarId = root.GetAvatarID();
        if (string.IsNullOrEmpty(avatarId)) return; // Can be empty during a swap
        if (avatarId == _lastAvatarId) return;
        _lastAvatarId = avatarId;
        OnNewAvatarLoaded(root.GetParameters().Select(x => (IParameterDefinition) x).ToArray());
    }

    private void OnTrackingDataUpdated(UnifiedTrackingData data) =>
        _customFaceExpressions.ForEach(x => OnParameterUpdated.Invoke(x.Name, x.GetWeight(data)));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_localSettingsService.ReadSetting<bool>("VRC_UseVRCFaceTracking")) return;
        await _standalone.InitializeAsync();
        UnifiedTracking.OnUnifiedDataUpdated += OnTrackingDataUpdated;
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(10, stoppingToken);
        }
        _standalone.Teardown();
    }
}

class VRCFaceTrackingModuleData : IModuleDataService
{
    public Task<IEnumerable<InstallableTrackingModule>> GetRemoteModules() =>
        Task.FromResult((IEnumerable<InstallableTrackingModule>) Array.Empty<InstallableTrackingModule>());
    public Task<int?> GetMyRatingAsync(TrackingModuleMetadata moduleMetadata) => Task.FromResult<int?>(null);
    public Task SetMyRatingAsync(TrackingModuleMetadata moduleMetadata, int rating) => Task.CompletedTask;
    public IEnumerable<InstallableTrackingModule> GetInstalledModules() => Array.Empty<InstallableTrackingModule>();
    public Task IncrementDownloadsAsync(TrackingModuleMetadata moduleMetadata) => Task.CompletedTask;

    public IEnumerable<InstallableTrackingModule> GetLegacyModules()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "VRCFaceTracking");
        if (!Directory.Exists(dir)) // "Eat my ass windows" -dfgHiatus
            Directory.CreateDirectory(dir);
        string[] files = Directory.GetFiles(dir, "*.dll");
        InstallableTrackingModule[] trackingModules = new InstallableTrackingModule[files.Length];
        for (int i = 0; i < files.Length; i++)
            trackingModules[i] = new InstallableTrackingModule
            {
                AssemblyLoadPath = Path.GetFullPath(files[i])
            };
        return trackingModules;
    }
}

class VRCFaceTrackingDispatcher : IDispatcherService
{
    private Contracts.IDispatcherService _dispatcherService;

    public VRCFaceTrackingDispatcher(Contracts.IDispatcherService dispatcherService) =>
        _dispatcherService = dispatcherService;

    public void Run(Action action) => _dispatcherService.Run(action);
}

class VRCFaceTrackingSettings : ILocalSettingsService
{
    private Contracts.ILocalSettingsService _localSettingsService;

    public VRCFaceTrackingSettings(Contracts.ILocalSettingsService settingsService) =>
        _localSettingsService = settingsService;

    public Task<T> ReadSettingAsync<T>(string key, T? defaultValue = default, bool forceLocal = false) =>
        Task.FromResult(_localSettingsService.ReadSetting(key, defaultValue, forceLocal));

    public Task SaveSettingAsync<T>(string key, T value, bool forceLocal = false)
    {
        _localSettingsService.SaveSetting(key, value, forceLocal);
        return Task.CompletedTask;
    }

    public Task Save(object target)
    {
        _localSettingsService.Save(target);
        return Task.CompletedTask;
    }

    public Task Load(object target)
    {
        _localSettingsService.Save(target);
        return Task.CompletedTask;
    }
}

/*
The following class is an excerpt from TigersUniverse/VRCFaceTracking

                                 Apache License
                           Version 2.0, January 2004
                        http://www.apache.org/licenses/

   TERMS AND CONDITIONS FOR USE, REPRODUCTION, AND DISTRIBUTION

   1. Definitions.

      "License" shall mean the terms and conditions for use, reproduction,
      and distribution as defined by Sections 1 through 9 of this document.

      "Licensor" shall mean the copyright owner or entity authorized by
      the copyright owner that is granting the License.

      "Legal Entity" shall mean the union of the acting entity and all
      other entities that control, are controlled by, or are under common
      control with that entity. For the purposes of this definition,
      "control" means (i) the power, direct or indirect, to cause the
      direction or management of such entity, whether by contract or
      otherwise, or (ii) ownership of fifty percent (50%) or more of the
      outstanding shares, or (iii) beneficial ownership of such entity.

      "You" (or "Your") shall mean an individual or Legal Entity
      exercising permissions granted by this License.

      "Source" form shall mean the preferred form for making modifications,
      including but not limited to software source code, documentation
      source, and configuration files.

      "Object" form shall mean any form resulting from mechanical
      transformation or translation of a Source form, including but
      not limited to compiled object code, generated documentation,
      and conversions to other media types.

      "Work" shall mean the work of authorship, whether in Source or
      Object form, made available under the License, as indicated by a
      copyright notice that is included in or attached to the work
      (an example is provided in the Appendix below).

      "Derivative Works" shall mean any work, whether in Source or Object
      form, that is based on (or derived from) the Work and for which the
      editorial revisions, annotations, elaborations, or other modifications
      represent, as a whole, an original work of authorship. For the purposes
      of this License, Derivative Works shall not include works that remain
      separable from, or merely link (or bind by name) to the interfaces of,
      the Work and Derivative Works thereof.

      "Contribution" shall mean any work of authorship, including
      the original version of the Work and any modifications or additions
      to that Work or Derivative Works thereof, that is intentionally
      submitted to Licensor for inclusion in the Work by the copyright owner
      or by an individual or Legal Entity authorized to submit on behalf of
      the copyright owner. For the purposes of this definition, "submitted"
      means any form of electronic, verbal, or written communication sent
      to the Licensor or its representatives, including but not limited to
      communication on electronic mailing lists, source code control systems,
      and issue tracking systems that are managed by, or on behalf of, the
      Licensor for the purpose of discussing and improving the Work, but
      excluding communication that is conspicuously marked or otherwise
      designated in writing by the copyright owner as "Not a Contribution."

      "Contributor" shall mean Licensor and any individual or Legal Entity
      on behalf of whom a Contribution has been received by Licensor and
      subsequently incorporated within the Work.

   2. Grant of Copyright License. Subject to the terms and conditions of
      this License, each Contributor hereby grants to You a perpetual,
      worldwide, non-exclusive, no-charge, royalty-free, irrevocable
      copyright license to reproduce, prepare Derivative Works of,
      publicly display, publicly perform, sublicense, and distribute the
      Work and such Derivative Works in Source or Object form.

   3. Grant of Patent License. Subject to the terms and conditions of
      this License, each Contributor hereby grants to You a perpetual,
      worldwide, non-exclusive, no-charge, royalty-free, irrevocable
      (except as stated in this section) patent license to make, have made,
      use, offer to sell, sell, import, and otherwise transfer the Work,
      where such license applies only to those patent claims licensable
      by such Contributor that are necessarily infringed by their
      Contribution(s) alone or by combination of their Contribution(s)
      with the Work to which such Contribution(s) was submitted. If You
      institute patent litigation against any entity (including a
      cross-claim or counterclaim in a lawsuit) alleging that the Work
      or a Contribution incorporated within the Work constitutes direct
      or contributory patent infringement, then any patent licenses
      granted to You under this License for that Work shall terminate
      as of the date such litigation is filed.

   4. Redistribution. You may reproduce and distribute copies of the
      Work or Derivative Works thereof in any medium, with or without
      modifications, and in Source or Object form, provided that You
      meet the following conditions:

      (a) You must give any other recipients of the Work or
          Derivative Works a copy of this License; and

      (b) You must cause any modified files to carry prominent notices
          stating that You changed the files; and

      (c) You must retain, in the Source form of any Derivative Works
          that You distribute, all copyright, patent, trademark, and
          attribution notices from the Source form of the Work,
          excluding those notices that do not pertain to any part of
          the Derivative Works; and

      (d) If the Work includes a "NOTICE" text file as part of its
          distribution, then any Derivative Works that You distribute must
          include a readable copy of the attribution notices contained
          within such NOTICE file, excluding those notices that do not
          pertain to any part of the Derivative Works, in at least one
          of the following places: within a NOTICE text file distributed
          as part of the Derivative Works; within the Source form or
          documentation, if provided along with the Derivative Works; or,
          within a display generated by the Derivative Works, if and
          wherever such third-party notices normally appear. The contents
          of the NOTICE file are for informational purposes only and
          do not modify the License. You may add Your own attribution
          notices within Derivative Works that You distribute, alongside
          or as an addendum to the NOTICE text from the Work, provided
          that such additional attribution notices cannot be construed
          as modifying the License.

      You may add Your own copyright statement to Your modifications and
      may provide additional or different license terms and conditions
      for use, reproduction, or distribution of Your modifications, or
      for any such Derivative Works as a whole, provided Your use,
      reproduction, and distribution of the Work otherwise complies with
      the conditions stated in this License.

   5. Submission of Contributions. Unless You explicitly state otherwise,
      any Contribution intentionally submitted for inclusion in the Work
      by You to the Licensor shall be under the terms and conditions of
      this License, without any additional terms or conditions.
      Notwithstanding the above, nothing herein shall supersede or modify
      the terms of any separate license agreement you may have executed
      with Licensor regarding such Contributions.

   6. Trademarks. This License does not grant permission to use the trade
      names, trademarks, service marks, or product names of the Licensor,
      except as required for reasonable and customary use in describing the
      origin of the Work and reproducing the content of the NOTICE file.

   7. Disclaimer of Warranty. Unless required by applicable law or
      agreed to in writing, Licensor provides the Work (and each
      Contributor provides its Contributions) on an "AS IS" BASIS,
      WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or
      implied, including, without limitation, any warranties or conditions
      of TITLE, NON-INFRINGEMENT, MERCHANTABILITY, or FITNESS FOR A
      PARTICULAR PURPOSE. You are solely responsible for determining the
      appropriateness of using or redistributing the Work and assume any
      risks associated with Your exercise of permissions under this License.

   8. Limitation of Liability. In no event and under no legal theory,
      whether in tort (including negligence), contract, or otherwise,
      unless required by applicable law (such as deliberate and grossly
      negligent acts) or agreed to in writing, shall any Contributor be
      liable to You for damages, including any direct, indirect, special,
      incidental, or consequential damages of any character arising as a
      result of this License or out of the use or inability to use the
      Work (including but not limited to damages for loss of goodwill,
      work stoppage, computer failure or malfunction, or any and all
      other commercial damages or losses), even if such Contributor
      has been advised of the possibility of such damages.

   9. Accepting Warranty or Additional Liability. While redistributing
      the Work or Derivative Works thereof, You may choose to offer,
      and charge a fee for, acceptance of support, warranty, indemnity,
      or other liability obligations and/or rights consistent with this
      License. However, in accepting such obligations, You may act only
      on Your own behalf and on Your sole responsibility, not on behalf
      of any other Contributor, and only if You agree to indemnify,
      defend, and hold each Contributor harmless for any liability
      incurred by, or claims asserted against, such Contributor by reason
      of your accepting any such warranty or additional liability.

   END OF TERMS AND CONDITIONS

   APPENDIX: How to apply the Apache License to your work.

      To apply the Apache License to your work, attach the following
      boilerplate notice, with the fields enclosed by brackets "[]"
      replaced with your own identifying information. (Don't include
      the brackets!)  The text should be enclosed in the appropriate
      comment syntax for the file format. We also recommend that a
      file or class name and description of purpose be included on the
      same "printed page" as the copyright notice for easier
      identification within third-party archives.

   Copyright 2024 benaclejames

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.

*/

public class MainIntegrated
{
    private static readonly CancellationTokenSource MasterCancellationTokenSource = new();
    private readonly ILogger _logger;
    private readonly ILibManager _libManager;
    private readonly UnifiedTrackingMutator _mutator;

    public MainIntegrated(ILoggerFactory loggerFactory, ILibManager libManager, UnifiedTrackingMutator mutator)
    {
        _logger = loggerFactory.CreateLogger("MainStandalone");
        _libManager = libManager;
        _mutator = mutator;
    }

    public async void Teardown()
    {
        _logger.LogInformation("VRCFT Standalone Exiting!");
        _libManager.TeardownAllAndResetAsync();

        await _mutator.Save();

        // Kill our threads
        _logger.LogDebug("Cancelling token sources...");
        MasterCancellationTokenSource.Cancel();

        _logger.LogDebug("Resetting our time end period...");
        if (OperatingSystem.IsWindows())
            VRCFaceTracking.Core.Utils.TimeEndPeriod(1);

        _logger.LogDebug("Teardown successful. Awaiting exit...");
    }

    public async Task InitializeAsync()
    {
        _libManager.Initialize();
        _mutator.Load();

        // Begin main update loop
        _logger.LogDebug("Starting update loop...");
        if (OperatingSystem.IsWindows())
            VRCFaceTracking.Core.Utils.TimeBeginPeriod(1);
        ThreadPool.QueueUserWorkItem(async ct =>
        {
            var token = (CancellationToken)ct;

            while (!token.IsCancellationRequested)
            {
                Thread.Sleep(10);
                await UnifiedTracking.UpdateData(token);
            }
        }, MasterCancellationTokenSource.Token);

        await Task.CompletedTask;
    }
}

/*
The following code is a modified excerpt from TigersUniverse/Hypernex.Unity

MIT License

Copyright (c) 2025 TigersUniverse

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

*/

public interface ICustomFaceExpression
{
    public string Name { get; }
    public float GetWeight(UnifiedTrackingData unifiedTrackingData);
    bool IsMatch(string parameterName);
}

internal static class VRCFTParameters
{
    public static bool UseBinary { get; set; } = true;
    public static Type ParameterType = typeof(VRCFTProgrammableExpression);

    public static ICustomFaceExpression[] UpdateParameters(IParameterDefinition[] parameters, ParameterVersion parameterVersion = ParameterVersion.Both)
    {
        Parameter[] parametersToPullFrom;
        switch (parameterVersion)
        {
            case ParameterVersion.v1:
                parametersToPullFrom = UnifiedTracking.AllParameters_v1;
                break;
            case ParameterVersion.v2:
                parametersToPullFrom = UnifiedTracking.AllParameters_v2;
                break;
            case ParameterVersion.Both:
                parametersToPullFrom =
                    UnifiedTracking.AllParameters_v2.Concat(UnifiedTracking.AllParameters_v1).ToArray();
                break;
            default:
                throw new Exception("Unknown ParameterVersion");
        }
        List<Parameter> paramList = new List<Parameter>();
        foreach (Parameter parameter in parametersToPullFrom)
            paramList.AddRange(parameter.ResetParam(parameters));
        List<VRCFTProgrammableExpression> customFaceExpressions = GetParameters(paramList.ToArray());
        return customFaceExpressions.Select(x => (ICustomFaceExpression) x).ToArray();
    }

    private static List<VRCFTProgrammableExpression> GetParameters(Parameter[] parametersToPullFrom)
    {
        List<VRCFTProgrammableExpression> expressions = new();
        foreach (Parameter vrcftParameter in parametersToPullFrom)
        {
            Type parameterType = GetRootTypeNoAbstractParameter(vrcftParameter.GetType());
            if (parameterType == typeof(BaseParam<float>))
            {
                BaseParam<float> paramLiteral = (BaseParam<float>) vrcftParameter;
                (Func<string>, Func<UnifiedTrackingData, float>) paramValue = GetBaseParamValue(paramLiteral);
                expressions.Add(new VRCFTProgrammableExpression(paramValue.Item1, paramValue.Item2));
            }
            else if (parameterType == typeof(BaseParam<bool>))
            {
                BaseParam<bool> paramLiteral = (BaseParam<bool>) vrcftParameter;
                (Func<string>, Func<UnifiedTrackingData, bool>) paramValue = GetBaseParamValue(paramLiteral);
                expressions.Add(new VRCFTProgrammableExpression(paramValue.Item1,
                    data => paramValue.Item2.Invoke(data) ? 1.0f : 0.0f));
            }
            else if (parameterType == typeof(BaseParam<Vector2>))
            {
                BaseParam<Vector2> paramLiteral = (BaseParam<Vector2>) vrcftParameter;
                (Func<string>, Func<UnifiedTrackingData, Vector2>) paramValue = GetBaseParamValue(paramLiteral);
                expressions.Add(new VRCFTProgrammableExpression(() => paramValue.Item1.Invoke() + "X",
                    data => paramValue.Item2.Invoke(data).x));
                expressions.Add(new VRCFTProgrammableExpression(() => paramValue.Item1.Invoke() + "Y",
                    data => paramValue.Item2.Invoke(data).y));
            }
            else if (UseBinary && parameterType == typeof(BinaryBaseParameter))
            {
                BinaryBaseParameter paramLiteral = (BinaryBaseParameter) vrcftParameter;
                foreach ((Func<string>, Func<UnifiedTrackingData, bool>) valueTuple in
                         GetBinaryBaseParamValue(paramLiteral))
                    expressions.Add(new VRCFTProgrammableExpression(valueTuple.Item1,
                        data => valueTuple.Item2.Invoke(data) ? 1.0f : 0.0f));
            }
        }
        foreach (Parameter vrcftParameter in parametersToPullFrom)
        {
            Type parameterType = GetRootTypeNoAbstractParameter(vrcftParameter.GetType());
            if (parameterType != typeof(EParam)) continue;
            EParam paramLiteral = (EParam) vrcftParameter;
            bool exists = false;
            foreach ((string, Parameter) parameter in paramLiteral.GetParamNames())
            {
                if (expressions.Select(x => x.Name).Contains(parameter.Item1))
                {
                    exists = true;
                    break;
                }
                foreach ((string, Parameter) valueTuple in parameter.Item2.GetParamNames())
                {
                    if (expressions.Select(x => x.Name).Contains(valueTuple.Item1))
                    {
                        exists = true;
                        break;
                    }
                    if (expressions.Select(x => x.Name).Contains(valueTuple.Item2.GetParamNames()[0].paramName))
                    {
                        exists = true;
                        break;
                    }
                }
            }
            if(exists) continue;
            foreach ((Func<string>, Func<UnifiedTrackingData, float>) valueTuple in GetEParamValue(paramLiteral))
                expressions.Add(new VRCFTProgrammableExpression(valueTuple.Item1, valueTuple.Item2));
        }
        return expressions;
    }

    private static Type GetRootTypeNoAbstractParameter(Type derivedType)
    {
        Type baseType = derivedType;
        Type? lastType = derivedType;
        while (lastType != null && lastType != typeof(Parameter) && lastType != typeof(Object))
        {
            baseType = lastType;
            lastType = baseType.BaseType;
        }
        return baseType;
    }

    private static (Func<string>, Func<UnifiedTrackingData, T>) GetBaseParamValue<T>(BaseParam<T> baseParam) where T : struct
    {
        Type paramType = GetRootTypeNoAbstractParameter(baseParam.GetType());
        Func<UnifiedTrackingData, T> getValueFunc =
            (Func<UnifiedTrackingData, T>) paramType.GetField("_getValueFunc",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(baseParam);
        return (() => baseParam.GetParamNames()[0].Item1, getValueFunc);
    }

    private static (Func<string>, Func<UnifiedTrackingData, bool>)[] GetBinaryBaseParamValue(BinaryBaseParameter baseParam)
    {
        List<(Func<string>, Func<UnifiedTrackingData, bool>)> binaryParameters = new();
        (string, Parameter)[] paramNames = baseParam.GetParamNames();
        foreach ((string, Parameter) valueTuple in paramNames)
        {
            BaseParam<bool> paramLiteral = (BaseParam<bool>) valueTuple.Item2;
            binaryParameters.Add((() => paramLiteral.GetParamNames()[0].Item1,
                (Func<UnifiedTrackingData, bool>) paramLiteral.GetType()
                    .GetField("_getValueFunc", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(paramLiteral)));
        }
        return binaryParameters.ToArray();
    }

    private static (Func<string>, Func<UnifiedTrackingData, float>)[] GetEParamValue(EParam eParam)
    {
        List<(Func<string>, Func<UnifiedTrackingData, float>)> eParameters = new();
        (string, Parameter)[] paramNames = eParam.GetParamNames();
        foreach ((string, Parameter) valueTuple in paramNames)
        {
            Type paramLiteralType = GetRootTypeNoAbstractParameter(valueTuple.Item2.GetType());
            if (paramLiteralType == typeof(BaseParam<float>))
            {
                (Func<string>, Func<UnifiedTrackingData, float>) baseParamFunc =
                    GetBaseParamValue((BaseParam<float>) valueTuple.Item2);
                eParameters.Add(baseParamFunc);
            }
            else if (UseBinary && paramLiteralType == typeof(BinaryBaseParameter))
            {
                BinaryBaseParameter binaryBaseParameter = (BinaryBaseParameter) valueTuple.Item2;
                foreach ((Func<string>, Func<UnifiedTrackingData, bool>) binaryTuple in GetBinaryBaseParamValue(
                             binaryBaseParameter))
                    eParameters.Add((binaryTuple.Item1, data => binaryTuple.Item2.Invoke(data) ? 1.0f : 0.0f));
            }
        }
        return eParameters.ToArray();
    }

    private class VRCFTProgrammableExpression : ICustomFaceExpression
    {
        private string? lastRegexName;
        private Regex? lastRegex;

        private Regex Regex
        {
            get
            {
                if (lastRegexName == null) lastRegexName = Name;
                if (lastRegex == null || lastRegexName != Name)
                {
                    lastRegexName = Name;
                    lastRegex = new Regex(@"(?<!(v\d+))(/" + lastRegexName + ")$|^(" + lastRegexName + ")$");
                    matches.Clear();
                }
                return lastRegex;
            }
        }

        private Dictionary<string, bool> matches = new();

        private Func<string> getNameFunc;
        private Func<UnifiedTrackingData, float> getWeightFunc;

        public VRCFTProgrammableExpression(Func<string> getNameFunc, Func<UnifiedTrackingData, float> getWeightFunc)
        {
            this.getNameFunc = getNameFunc;
            this.getWeightFunc = getWeightFunc;
        }

        public string Name => getNameFunc.Invoke();
        public float GetWeight(UnifiedTrackingData unifiedTrackingData) => getWeightFunc.Invoke(unifiedTrackingData);

        public bool IsMatch(string parameterName)
        {
            bool match;
            if (!matches.TryGetValue(parameterName, out match))
            {
                match = Regex.IsMatch(parameterName);
                matches.Add(parameterName, match);
            }
            return match;
        }
    }

    public enum ParameterVersion
    {
        [Obsolete] v1,
        v2,
        Both
    }
}
