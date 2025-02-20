﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using LegendaryExplorer.SharedUI.Interfaces;
using LegendaryExplorerCore.Gammtek.IO;
using LegendaryExplorerCore.Helpers;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Unreal;

namespace LegendaryExplorer.UserControls.ExportLoaderControls;

public partial class BinaryInterpreterWPF
{

    private List<ITreeItem> StartShaderCacheScanStream(byte[] data, ref int binarystart)
    {
        var subnodes = new List<ITreeItem>();
        try
        {
            int dataOffset = CurrentLoadedExport.DataOffset;
            var bin = new EndianReader(new MemoryStream(data)) { Endian = CurrentLoadedExport.FileRef.Endian };
            bin.JumpTo(binarystart);

            if (CurrentLoadedExport.Game == MEGame.UDK)
            {
                subnodes.Add(new BinInterpNode(bin.Position, $"UDK Unknown: {bin.ReadInt32()}"));
            }

            if (CurrentLoadedExport.Game.IsLEGame())
            {
                subnodes.Add(new BinInterpNode(bin.Position, $"Platform: {(EShaderPlatformLE)bin.ReadByte()}")
                    { Length = 1 });
            }
            else
            {
                subnodes.Add(new BinInterpNode(bin.Position, $"Platform: {(EShaderPlatformOT)bin.ReadByte()}")
                    { Length = 1 });
            }

            int mapCount = Pcc.Game is MEGame.ME3 || Pcc.Game.IsLEGame() ? 2 : 1;
            var nameMappings = new[] { "CompressedCacheMap", "ShaderTypeCRCMap" }; // hack...
            while (mapCount > 0)
            {
                mapCount--;
                int vertexMapCount = bin.ReadInt32();
                var mappingNode = new BinInterpNode(bin.Position - 4, $"{nameMappings[mapCount]}, {vertexMapCount} items");
                subnodes.Add(mappingNode);

                for (int i = 0; i < vertexMapCount; i++)
                {
                    //if (i > 1000)
                    //    continue;
                    NameReference shaderName = bin.ReadNameReference(Pcc);
                    int shaderCRC = bin.ReadInt32();
                    mappingNode.Items.Add(new BinInterpNode(bin.Position - 12, $"CRC:{shaderCRC:X8} {shaderName.Instanced}") { Length = 12 });
                }
            }

            if (Pcc.Game == MEGame.ME1)
            {
                ReadVertexFactoryMap();
            }

            int embeddedShaderFileCount = bin.ReadInt32();
            var embeddedShaderCount = new BinInterpNode(bin.Position - 4, $"Embedded Shader File Count: {embeddedShaderFileCount}");
            subnodes.Add(embeddedShaderCount);
            for (int i = 0; i < embeddedShaderFileCount; i++)
            {
                NameReference shaderName = bin.ReadNameReference(Pcc);
                var shaderNode = new BinInterpNode(bin.Position - 8, $"Shader {i} {shaderName.Instanced}");
                embeddedShaderCount.Items.Add(shaderNode);

                shaderNode.Items.Add(new BinInterpNode(bin.Position - 8, $"Shader Type: {shaderName.Instanced}")
                    { Length = 8 });
                shaderNode.Items.Add(new BinInterpNode(bin.Position, $"Shader GUID {bin.ReadGuid()}")
                    { Length = 16 });
                if (Pcc.Game == MEGame.UDK)
                {
                    shaderNode.Items.Add(MakeGuidNode(bin, "2nd Guid?"));
                    shaderNode.Items.Add(MakeUInt32Node(bin, "unk?"));
                }

                int shaderEndOffset = bin.ReadInt32();
                shaderNode.Items.Add(new BinInterpNode(bin.Position - 4, $"Shader End Offset: {shaderEndOffset}")
                    { Length = 4 });

                if (CurrentLoadedExport.Game == MEGame.UDK)
                {
                    // UDK 2015 SM3 cache has what appears to be a count followed by...
                    // two pairs of ushorts?

                    int udkCount = bin.ReadInt32();
                    var udkCountNode = new BinInterpNode(bin.Position - 4, $"Some UDK count: {udkCount}");
                    shaderNode.Items.Add(udkCountNode);

                    for (int j = 0; j < udkCount; j++)
                    {
                        udkCountNode.Items.Add(MakeUInt16Node(bin, $"UDK Count[{j}]"));
                    }

                    shaderNode.Items.Add(MakeUInt16Node(bin, $"UDK Unknown Post Count Thing"));
                }
                else
                {
                    if (CurrentLoadedExport.Game.IsLEGame())
                    {
                        shaderNode.Items.Add(new BinInterpNode(bin.Position,
                                $"Platform: {(EShaderPlatformLE)bin.ReadByte()}")
                            { Length = 1 });
                    }
                    else
                    {
                        shaderNode.Items.Add(new BinInterpNode(bin.Position,
                                $"Platform: {(EShaderPlatformOT)bin.ReadByte()}")
                            { Length = 1 });
                    }

                    shaderNode.Items.Add(new BinInterpNode(bin.Position,
                            $"Frequency: {(EShaderFrequency)bin.ReadByte()}")
                        { Length = 1 });
                }

                int shaderSize = bin.ReadInt32();
                shaderNode.Items.Add(new BinInterpNode(bin.Position - 4, $"Shader File Size: {shaderSize}")
                    { Length = 4 });

                shaderNode.Items.Add(new BinInterpNode(bin.Position, "Shader File") { Length = shaderSize });
                bin.Skip(shaderSize);

                shaderNode.Items.Add(MakeInt32Node(bin, "ParameterMap CRC"));

                shaderNode.Items.Add(new BinInterpNode(bin.Position, $"Shader End GUID: {bin.ReadGuid()}")
                    { Length = 16 });

                string shaderType;
                shaderNode.Items.Add(new BinInterpNode(bin.Position,
                        $"Shader Type: {shaderType = bin.ReadNameReference(Pcc)}")
                    { Length = 8 });

                shaderNode.Items.Add(MakeInt32Node(bin, "Number of Instructions"));

                //DONT DELETE. Shader Parameter research is ongoing, but display is disabled when it's not actively being worked on, as it still has lots of bugs
                //if (ReadShaderParameters(bin, shaderType, out Exception e) is BinInterpNode paramsNode)
                //{
                //    shaderNode.Items.Add(paramsNode);
                //    if (bin.Position != shaderEndOffset - dataOffset)
                //    {
                //        Debugger.Break();
                //    }
                //}
                //if (e is not null)
                //{
                //    throw e;
                //}

                if (bin.Position != (shaderEndOffset - dataOffset))
                {
                    var unparsedShaderParams =
                        new BinInterpNode(bin.Position,
                                $"Unparsed Shader Parameters ({shaderEndOffset - dataOffset - bin.Position} bytes)")
                            { Length = (shaderEndOffset - dataOffset) - (int)bin.Position };
                    shaderNode.Items.Add(unparsedShaderParams);
                    while (bin.Position + dataOffset < shaderEndOffset)
                    {
                        var param = new BinInterpNode(bin.Position, $"Param");
                        unparsedShaderParams.Items.Add(param);
                        param.Items.Add(MakeInt16Node(bin, "BaseIndex"));
                        param.Items.Add(MakeInt16Node(bin, "NumResources"));
                        param.Items.Add(MakeInt16Node(bin, "SamplerIndex"));
                    }
                }

                bin.JumpTo(shaderEndOffset - dataOffset);
            }

            void ReadVertexFactoryMap()
            {
                int vertexFactoryMapCount = bin.ReadInt32();
                var factoryMapNode = new BinInterpNode(bin.Position - 4, $"Vertex Factory Name Mapping, {vertexFactoryMapCount} items");
                subnodes.Add(factoryMapNode);

                for (int i = 0; i < vertexFactoryMapCount; i++)
                {
                    NameReference shaderName = bin.ReadNameReference(Pcc);
                    int shaderCRC = bin.ReadInt32();
                    factoryMapNode.Items.Add(new BinInterpNode(bin.Position - 12, $"{shaderCRC:X8} {shaderName.Instanced}") { Length = 12 });
                }
            }

            if (Pcc.Game is MEGame.ME2 or MEGame.ME3 or MEGame.LE1 or MEGame.LE2 or MEGame.LE3)
            {
                ReadVertexFactoryMap();
            }

            int materialShaderMapcount = bin.ReadInt32();
            var materialShaderMaps = new BinInterpNode(bin.Position - 4, $"Material Shader Maps, {materialShaderMapcount} items");
            subnodes.Add(materialShaderMaps);
            for (int i = 0; i < materialShaderMapcount; i++)
            {
                var nodes = new List<ITreeItem>();
                materialShaderMaps.Items.Add(new BinInterpNode(bin.Position, $"Material Shader Map {i}") { Items = nodes });
                nodes.Add(ReadFStaticParameterSet(bin));

                if (Pcc.Game >= MEGame.ME3)
                {
                    nodes.Add(new BinInterpNode(bin.Position, $"Unreal Version {bin.ReadInt32()}") { Length = 4 });
                    nodes.Add(new BinInterpNode(bin.Position, $"Licensee Version {bin.ReadInt32()}") { Length = 4 });
                }

                int shaderMapEndOffset = bin.ReadInt32();
                nodes.Add(new BinInterpNode(bin.Position - 4, $"Material Shader Map end offset {shaderMapEndOffset}") { Length = 4 });

                int unkCount = bin.ReadInt32();
                var unkNodes = new List<ITreeItem>();
                nodes.Add(new BinInterpNode(bin.Position - 4, $"Shaders {unkCount}") { Length = 4, Items = unkNodes });
                for (int j = 0; j < unkCount; j++)
                {
                    unkNodes.Add(new BinInterpNode(bin.Position, $"Shader Type: {bin.ReadNameReference(Pcc).Instanced}") { Length = 8 });
                    unkNodes.Add(new BinInterpNode(bin.Position, $"GUID: {bin.ReadGuid()}") { Length = 16 });
                    unkNodes.Add(new BinInterpNode(bin.Position, $"Shader Type: {bin.ReadNameReference(Pcc).Instanced}") { Length = 8 });
                }

                int meshShaderMapsCount = bin.ReadInt32();
                var meshShaderMaps = new BinInterpNode(bin.Position - 4, $"Mesh Shader Maps, {meshShaderMapsCount} items") { Length = 4 };
                nodes.Add(meshShaderMaps);
                for (int j = 0; j < meshShaderMapsCount; j++)
                {
                    var nodes2 = new List<ITreeItem>();
                    meshShaderMaps.Items.Add(new BinInterpNode(bin.Position, $"Mesh Shader Map {j}") { Items = nodes2 });

                    int shaderCount = bin.ReadInt32();
                    var shaders = new BinInterpNode(bin.Position - 4, $"Shaders, {shaderCount} items") { Length = 4 };
                    nodes2.Add(shaders);
                    for (int k = 0; k < shaderCount; k++)
                    {
                        var nodes3 = new List<ITreeItem>();
                        shaders.Items.Add(new BinInterpNode(bin.Position, $"Shader {k}") { Items = nodes3 });

                        nodes3.Add(new BinInterpNode(bin.Position, $"Shader Type: {bin.ReadNameReference(Pcc)}") { Length = 8 });
                        nodes3.Add(new BinInterpNode(bin.Position, $"GUID: {bin.ReadGuid()}") { Length = 16 });
                        nodes3.Add(new BinInterpNode(bin.Position, $"Shader Type: {bin.ReadNameReference(Pcc)}") { Length = 8 });
                    }
                    nodes2.Add(new BinInterpNode(bin.Position, $"Vertex Factory Type: {bin.ReadNameReference(Pcc)}") { Length = 8 });
                    if (Pcc.Game == MEGame.ME1)
                    {
                        nodes2.Add(MakeUInt32Node(bin, "Unk"));
                    }
                }

                nodes.Add(new BinInterpNode(bin.Position, $"MaterialId: {bin.ReadGuid()}") { Length = 16 });

                nodes.Add(MakeStringNode(bin, "Friendly Name"));

                nodes.Add(ReadFStaticParameterSet(bin));

                if (Pcc.Game >= MEGame.ME3)
                {
                    string[] uniformExpressionArrays =
                    [
                        "UniformPixelVectorExpressions",
                        "UniformPixelScalarExpressions",
                        "Uniform2DTextureExpressions",
                        "UniformCubeTextureExpressions",
                        "UniformVertexVectorExpressions",
                        "UniformVertexScalarExpressions"
                    ];

                    foreach (string uniformExpressionArrayName in uniformExpressionArrays)
                    {
                        int expressionCount = bin.ReadInt32();
                        nodes.Add(new BinInterpNode(bin.Position - 4, $"{uniformExpressionArrayName}, {expressionCount} expressions")
                        {
                            Items = ReadList(expressionCount, x => ReadMaterialUniformExpression(bin))
                        });
                    }
                    if (Pcc.Game.IsLEGame())
                    {
                        nodes.Add(new BinInterpNode(bin.Position, $"Platform: {(EShaderPlatformLE)bin.ReadInt32()}") { Length = 4 });
                    }
                    else if (Pcc.Game is not MEGame.ME1)
                    {
                        nodes.Add(new BinInterpNode(bin.Position, $"Platform: {(EShaderPlatformOT)bin.ReadInt32()}") { Length = 4 });
                    }
                }

                bin.JumpTo(shaderMapEndOffset - dataOffset);
            }

            if (CurrentLoadedExport.Game is MEGame.ME3 or MEGame.UDK && CurrentLoadedExport.FileRef.Platform != MEPackage.GamePlatform.Xenon)
            {
                int numShaderCachePayloads = bin.ReadInt32();
                var shaderCachePayloads = new BinInterpNode(bin.Position - 4, $"Shader Cache Payloads, {numShaderCachePayloads} items");
                subnodes.Add(shaderCachePayloads);
                for (int i = 0; i < numShaderCachePayloads; i++)
                {
                    shaderCachePayloads.Items.Add(MakeEntryNode(bin, $"Payload {i}"));
                }
            }
            else if (CurrentLoadedExport.Game == MEGame.ME1 && CurrentLoadedExport.FileRef.Platform != MEPackage.GamePlatform.PS3)
            {
                int numSomething = bin.ReadInt32();
                var somethings = new BinInterpNode(bin.Position - 4, $"Something, {numSomething} items");
                subnodes.Add(somethings);
                for (int i = 0; i < numSomething; i++)
                {
                    var node = new BinInterpNode(bin.Position, $"Something {i}");
                    node.Items.Add(MakeNameNode(bin, "SomethingName?"));
                    node.Items.Add(MakeGuidNode(bin, "SomethingGuid?"));
                    somethings.Items.Add(node);
                }
            }
        }
        catch (Exception ex)
        {
            subnodes.Add(new BinInterpNode { Header = $"Error reading binary data: {ex}" });
        }

        return subnodes;
    }

    private BinInterpNode ReadShaderParameters(EndianReader bin, string shaderType, out Exception exception)
    {
        exception = null;
        if (CurrentLoadedExport.Game is not MEGame.LE3)
        {
            return null;
        }

        var node = new BinInterpNode(bin.Position, "ShaderParameters") { IsExpanded = true };

        try
        {
            switch (shaderType)
            {
                case "FGFxPixelShaderSDRGFx_PS_CxformMultiply2Texture":
                case "FGFxPixelShaderSDRGFx_PS_CxformGouraudMultiplyTexture":
                case "FGFxPixelShaderSDRGFx_PS_CxformGouraud":
                case "FGFxPixelShaderSDRGFx_PS_TextTexture":
                case "FGFxPixelShaderSDRGFx_PS_CxformGouraudMultiplyNoAddAlpha":
                case "FGFxPixelShaderSDRGFx_PS_CxformGouraudMultiply":
                case "FGFxPixelShaderSDRGFx_PS_CxformGouraudNoAddAlpha":
                case "FGFxPixelShaderSDRGFx_PS_Cxform2Texture":
                case "FGFxPixelShaderSDRGFx_PS_CxformGouraudTexture":
                case "FGFxPixelShaderSDRGFx_PS_TextTextureSRGBMultiply":
                case "FGFxPixelShaderSDRGFx_PS_TextTextureSRGB":
                case "FGFxPixelShaderSDRGFx_PS_TextTextureColorMultiply":
                case "FGFxPixelShaderSDRGFx_PS_TextTextureColor":
                case "FGFxPixelShaderSDRGFx_PS_CxformTextureMultiply":
                case "FGFxPixelShaderSDRGFx_PS_CxformTexture":
                case "FGFxPixelShaderSDRGFx_PS_SolidColor":
                    for (int i = 0; i < 4; i++)
                    {
                        node.Items.Add(FShaderResourceParameter($"TextureParams[{i}]"));
                    }
                    node.Items.Add(FShaderParameter("ConstantColorParameter"));
                    node.Items.Add(FShaderParameter("ColorScaleParameter"));
                    node.Items.Add(FShaderParameter("ColorBiasParameter"));
                    node.Items.Add(FShaderParameter("InverseGammaParameter"));
                    break;
                case "THeightFogPixelShader<4>":
                case "THeightFogPixelShader<1>":
                    node.Items.Add(FSceneTextureShaderParameters("SceneTextureParameters"));
                    node.Items.Add(FShaderParameter("FogDistanceScaleParameter"));
                    node.Items.Add(FShaderParameter("FogExtinctionDistanceParameter"));
                    node.Items.Add(FShaderParameter("FogInScatteringParameter"));
                    node.Items.Add(FShaderParameter("FogStartDistanceParameter"));
                    node.Items.Add(FShaderParameter("FogMinStartDistanceParameter"));
                    node.Items.Add(FShaderParameter("EncodePowerParameter"));
                    node.Items.Add(FShaderParameter("unk1"));
                    break;
                case "TBranchingPCFModProjectionPixelShaderFSpotLightPolicyFHighQualityManualPCF":
                case "TBranchingPCFModProjectionPixelShaderFSpotLightPolicyFHighQualityFetch4PCF":
                case "TBranchingPCFModProjectionPixelShaderFSpotLightPolicyFHighQualityHwPCF":
                case "TBranchingPCFModProjectionPixelShaderFSpotLightPolicyFMediumQualityManualPCF":
                case "TBranchingPCFModProjectionPixelShaderFSpotLightPolicyFMediumQualityFetch4PCF":
                case "TBranchingPCFModProjectionPixelShaderFSpotLightPolicyFMediumQualityHwPCF":
                case "TBranchingPCFModProjectionPixelShaderFSpotLightPolicyFLowQualityManualPCF":
                case "TBranchingPCFModProjectionPixelShaderFSpotLightPolicyFLowQualityFetch4PCF":
                case "TBranchingPCFModProjectionPixelShaderFSpotLightPolicyFLowQualityHwPCF":
                    FBranchingPCFModProjectionPixelShader();
                    FSpotLightPolicy_ModShadowPixelParamsType();
                    break;
                case "FGFxVertexShader<GFx_VS_Glyph>":
                case "FGFxVertexShader<GFx_VS_XY16iCF32_T2>":
                case "FGFxVertexShader<GFx_VS_XY16iCF32_NoTexNoAlpha>":
                case "FGFxVertexShader<GFx_VS_Strip>":
                case "FGFxVertexShader<GFx_VS_XY16iCF32_NoTex>":
                case "FGFxVertexShader<GFx_VS_XY16iCF32>":
                case "FGFxVertexShader<GFx_VS_XY16iC32>":
                    node.Items.Add(FShaderParameter("TransformParameter"));
                    node.Items.Add(FShaderParameter("TextureMatrixParams[0]"));
                    node.Items.Add(FShaderParameter("TextureMatrixParams[1]"));
                    break;
                case "FResolveVertexShader":
                case "FReconstructHDRVertexShader":
                case "FLDRExtractVertexShader":
                case "FMotionBlurVertexShader":
                case "FBinkVertexShader":
                case "FOneColorVertexShader":
                case "FGammaCorrectionVertexShader":
                case "FNULLPixelShader":
                case "FHorizonBasedAOVertexShader":
                case "FModShadowVolumeVertexShader":
                case "FOcclusionQueryVertexShader<0>":
                case "FOcclusionQueryVertexShader<NUM_CUBE_VERTICES>":
                case "FModShadowProjectionVertexShader":
                case "FLUTBlenderVertexShader":
                case "FPostProcessAAVertexShader":
                case "FShadowProjectionVertexShader":
                case "FScreenVertexShader":
                case "FFluidVertexShader":
                case "FEdgePreservingFilterVertexShader":
                case "FLightFunctionVertexShader":
                    //These types have no params
                    return null;
                    break;
                case "TBranchingPCFModProjectionPixelShaderFDirectionalLightPolicyFMediumQualityManualPCF":
                case "TBranchingPCFModProjectionPixelShaderFDirectionalLightPolicyFHighQualityManualPCF":
                case "TBranchingPCFModProjectionPixelShaderFDirectionalLightPolicyFLowQualityManualPCF":
                case "TBranchingPCFModProjectionPixelShaderFDirectionalLightPolicyFHighQualityFetch4PCF":
                case "TBranchingPCFModProjectionPixelShaderFDirectionalLightPolicyFHighQualityHwPCF":
                case "TBranchingPCFModProjectionPixelShaderFDirectionalLightPolicyFMediumQualityFetch4PCF":
                case "TBranchingPCFModProjectionPixelShaderFDirectionalLightPolicyFMediumQualityHwPCF":
                case "TBranchingPCFModProjectionPixelShaderFDirectionalLightPolicyFLowQualityFetch4PCF":
                case "TBranchingPCFModProjectionPixelShaderFDirectionalLightPolicyFLowQualityHwPCF":
                    FBranchingPCFModProjectionPixelShader();
                    //FDirectionalLightPolicy::ModShadowPixelParamsType has no params
                    break;
                case "FResolveSingleSamplePixelShader":
                    node.Items.Add(FShaderResourceParameter("UnresolvedSurfaceParameter"));
                    node.Items.Add(FShaderParameter("SingleSampleIndexParameter"));
                    break;
                case "FResolveDepthPixelShader":
                    node.Items.Add(FShaderResourceParameter("UnresolvedSurfaceParameter"));
                    break;
                case "TModShadowVolumePixelShaderFPointLightPolicy":
                    FModShadowVolumePixelShader_Maybe();
                    FPointLightPolicy_ModShadowPixelParamsType();
                    break;
                case "FGammaCorrectionPixelShader":
                    node.Items.Add(FShaderResourceParameter("SceneTextureParameter"));
                    node.Items.Add(FShaderParameter("InverseGammaParameter"));
                    node.Items.Add(FShaderParameter("ColorScaleParameter"));
                    node.Items.Add(FShaderParameter("OverlayColorParameter"));
                    break;
                case "FGFxPixelShaderHDRGFx_PS_CxformMultiply2Texture":
                case "FGFxPixelShaderHDRGFx_PS_CxformGouraudMultiplyTexture":
                case "FGFxPixelShaderHDRGFx_PS_CxformGouraud":
                case "FGFxPixelShaderHDRGFx_PS_CxformGouraudMultiplyNoAddAlpha":
                case "FGFxPixelShaderHDRGFx_PS_CxformGouraudNoAddAlpha":
                case "FGFxPixelShaderHDRGFx_PS_CxformGouraudMultiply":
                case "FGFxPixelShaderHDRGFx_PS_Cxform2Texture":
                case "FGFxPixelShaderHDRGFx_PS_CxformGouraudTexture":
                case "FGFxPixelShaderHDRGFx_PS_CxformTexture":
                case "FGFxPixelShaderHDRGFx_PS_TextTextureSRGBMultiply":
                case "FGFxPixelShaderHDRGFx_PS_CxformTextureMultiply":
                case "FGFxPixelShaderHDRGFx_PS_TextTextureSRGB":
                case "FGFxPixelShaderHDRGFx_PS_TextTextureColorMultiply":
                case "FGFxPixelShaderHDRGFx_PS_TextTextureColor":
                case "FGFxPixelShaderHDRGFx_PS_TextTexture":
                case "FGFxPixelShaderHDRGFx_PS_SolidColor":
                    for (int i = 0; i < 4; i++)
                    {
                        node.Items.Add(FShaderResourceParameter($"TextureParams[{i}]"));
                    }
                    node.Items.Add(FShaderParameter("ConstantColorParameter"));
                    node.Items.Add(FShaderParameter("ColorScaleParameter"));
                    node.Items.Add(FShaderParameter("ColorBiasParameter"));
                    node.Items.Add(FShaderParameter("InverseGammaParameter"));
                    node.Items.Add(FShaderParameter("HDRBrightnessScaleParameter"));
                    break;
                case "FDownsampleSceneDepthPixelShader":
                    node.Items.Add(FShaderParameter("ProjectionScaleBiasParameter"));
                    node.Items.Add(FShaderParameter("SourceTexelOffsets01Parameter"));
                    node.Items.Add(FShaderParameter("SourceTexelOffsets23Parameter"));
                    node.Items.Add(FSceneTextureShaderParameters("SceneTextureParameters"));
                    break;
                case "FFXAA3BlendPixelShader":
                    node.Items.Add(FSceneTextureShaderParameters("SceneTextureParameters"));
                    node.Items.Add(FShaderParameter("unk1"));
                    node.Items.Add(FShaderParameter("unk2"));
                    break;
                case "TBranchingPCFModProjectionPixelShaderFPointLightPolicyFLowQualityFetch4PCF":
                case "TBranchingPCFModProjectionPixelShaderFPointLightPolicyFLowQualityHwPCF":
                case "TBranchingPCFModProjectionPixelShaderFPointLightPolicyFLowQualityManualPCF":
                case "TBranchingPCFModProjectionPixelShaderFPointLightPolicyFMediumQualityManualPCF":
                case "TBranchingPCFModProjectionPixelShaderFPointLightPolicyFMediumQualityFetch4PCF":
                case "TBranchingPCFModProjectionPixelShaderFPointLightPolicyFMediumQualityHwPCF":
                case "TBranchingPCFModProjectionPixelShaderFPointLightPolicyFHighQualityManualPCF":
                case "TBranchingPCFModProjectionPixelShaderFPointLightPolicyFHighQualityFetch4PCF":
                case "TBranchingPCFModProjectionPixelShaderFPointLightPolicyFHighQualityHwPCF":
                    FBranchingPCFModProjectionPixelShader();
                    FPointLightPolicy_ModShadowPixelParamsType();
                    break;
                case "FTexturedCalibrationBoxHDRPixelShader":
                    node.Items.Add(FShaderParameter("unk1"));
                    node.Items.Add(FShaderResourceParameter("unk2"));
                    break;
                case "FScreenPixelShader":
                case "FHBAOApplyPixelShader":
                case "FCopyVariancePixelShader":
                case "FSimpleElementHitProxyPixelShader":
                    node.Items.Add(FShaderResourceParameter("TextureParameter"));
                    break;
                case "FMotionBlurPixelShader":
                case "FMotionBlurPixelShaderDynamicVelocitiesOnly":
                    node.Items.Add(FSceneTextureShaderParameters("SceneTextureParameters"));
                    node.Items.Add(FMotionBlurShaderParameters("MotionBlurParameters"));
                    break;
                case "FDownsampleDepthVertexShader":
                    node.Items.Add(FShaderParameter("HalfSceneColorTexelSizeParameter"));
                    break;
                case "FAmbientOcclusionVertexShader":
                    node.Items.Add(FShaderParameter("ScreenToViewParameter"));
                    break;
                case "FCalibrationBoxHDRPixelShader":
                    node.Items.Add(FShaderParameter("unk"));
                    break;
                case "TFilterVertexShader<16>":
                case "TFilterVertexShader<15>":
                case "TFilterVertexShader<14>":
                case "TFilterVertexShader<13>":
                case "TFilterVertexShader<12>":
                case "TFilterVertexShader<11>":
                case "TFilterVertexShader<10>":
                case "TFilterVertexShader<9>":
                case "TFilterVertexShader<8>":
                case "TFilterVertexShader<7>":
                case "TFilterVertexShader<6>":
                case "TFilterVertexShader<5>":
                case "TFilterVertexShader<4>":
                case "TFilterVertexShader<3>":
                case "TFilterVertexShader<2>":
                case "TFilterVertexShader<1>":
                case "TDOFAndBloomGatherVertexShader<MAX_FILTER_SAMPLES>":
                case "TDOFAndBloomGatherVertexShader<NumFPFilterSamples>":
                case "TDOFAndBloomGatherVertexShader<1>":
                    node.Items.Add(FShaderParameter("SampleOffsetsParameter"));
                    break;
                case "FShaderComplexityAccumulatePixelShader":
                    node.Items.Add(FShaderParameter("NormalizedComplexityParameter"));
                    break;
                case "FDistortionApplyScreenVertexShader":
                case "FSimpleElementVertexShader":
                    node.Items.Add(FShaderParameter("TransformParameter"));
                    break;
                case "FDownsampleLightShaftsVertexShader":
                    node.Items.Add(FShaderParameter("ScreenToWorldParameter"));
                    break;
                case "FRadialBlurVertexShader":
                    node.Items.Add(FShaderParameter("WorldCenterPosParameter"));
                    break;
                case "FOneColorPixelShader":
                    node.Items.Add(FShaderParameter("ColorParameter"));
                    break;
                case "FDOFAndBloomBlendVertexShader":
                    node.Items.Add(FShaderParameter("SceneCoordinateScaleBiasParameter"));
                    break;
                case "FHistoryUpdateVertexShader":
                    node.Items.Add(FShaderParameter("ScreenToWorldOffsetParameter"));
                    break;
                case "FReconstructHDRPixelShader<FALSE>":
                case "FReconstructHDRPixelShader<TRUE>":
                    node.Items.Add(FShaderResourceParameter("unk1"));
                    node.Items.Add(FShaderParameter("unk2"));
                    node.Items.Add(FShaderParameter("unk3"));
                    break;
                case "FSimpleElementPixelShader":
                    node.Items.Add(FShaderResourceParameter("TextureParameter"));
                    node.Items.Add(FShaderParameter("TextureComponentReplicateParameter"));
                    node.Items.Add(FShaderParameter("TextureComponentReplicateAlphaParameter"));
                    break;
                case "TFilterPixelShader<16>":
                case "TFilterPixelShader<15>":
                case "TFilterPixelShader<14>":
                case "TFilterPixelShader<13>":
                case "TFilterPixelShader<12>":
                case "TFilterPixelShader<11>":
                case "TFilterPixelShader<10>":
                case "TFilterPixelShader<9>":
                case "TFilterPixelShader<8>":
                case "TFilterPixelShader<7>":
                case "TFilterPixelShader<6>":
                case "TFilterPixelShader<5>":
                case "TFilterPixelShader<4>":
                case "TFilterPixelShader<3>":
                case "TFilterPixelShader<2>":
                case "TFilterPixelShader<1>":
                    node.Items.Add(FShaderResourceParameter("FilterTextureParameter"));
                    node.Items.Add(FShaderParameter("SampleWeightsParameter"));
                    node.Items.Add(FShaderParameter("SampleMaskRectParameter"));
                    break;
                case "FShadowVolumeVertexShader":
                    node.Items.Add(FShaderParameter("unk1"));
                    node.Items.Add(FShaderParameter("unk2"));
                    node.Items.Add(FShaderParameter("unk3"));
                    break;
                case "FSFXUberPostProcessBlendPixelShader0011111":
                case "FSFXUberPostProcessBlendPixelShader0101001":
                case "FSFXUberPostProcessBlendPixelShader1111001":
                case "FSFXUberPostProcessBlendPixelShader1010001":
                case "FSFXUberPostProcessBlendPixelShader0110010":
                case "FSFXUberPostProcessBlendPixelShader1100010":
                case "FSFXUberPostProcessBlendPixelShader0011100":
                case "FSFXUberPostProcessBlendPixelShader0101010":
                case "FSFXUberPostProcessBlendPixelShader1111010":
                case "FSFXUberPostProcessBlendPixelShader1010010":
                case "FSFXUberPostProcessBlendPixelShader0010110":
                case "FSFXUberPostProcessBlendPixelShader0101111":
                case "FSFXUberPostProcessBlendPixelShader1111111":
                case "FSFXUberPostProcessBlendPixelShader1010111":
                case "FSFXUberPostProcessBlendPixelShader0100011":
                case "FSFXUberPostProcessBlendPixelShader1110011":
                case "FSFXUberPostProcessBlendPixelShader1011011":
                case "FSFXUberPostProcessBlendPixelShader1001111":
                case "FSFXUberPostProcessBlendPixelShader1000101":
                case "FSFXUberPostProcessBlendPixelShader0101100":
                case "FSFXUberPostProcessBlendPixelShader1111100":
                case "FSFXUberPostProcessBlendPixelShader1010100":
                case "FSFXUberPostProcessBlendPixelShader0011110":
                case "FSFXUberPostProcessBlendPixelShader0101000":
                case "FSFXUberPostProcessBlendPixelShader1111000":
                case "FSFXUberPostProcessBlendPixelShader1010000":
                case "FSFXUberPostProcessBlendPixelShader0110011":
                case "FSFXUberPostProcessBlendPixelShader1100011":
                case "FSFXUberPostProcessBlendPixelShader0011101":
                case "FSFXUberPostProcessBlendPixelShader0101011":
                case "FSFXUberPostProcessBlendPixelShader1111011":
                case "FSFXUberPostProcessBlendPixelShader1010011":
                case "FSFXUberPostProcessBlendPixelShader0010111":
                case "FSFXUberPostProcessBlendPixelShader0101110":
                case "FSFXUberPostProcessBlendPixelShader1111110":
                case "FSFXUberPostProcessBlendPixelShader1010110":
                case "FSFXUberPostProcessBlendPixelShader0100010":
                case "FSFXUberPostProcessBlendPixelShader1110010":
                case "FSFXUberPostProcessBlendPixelShader1011010":
                case "FSFXUberPostProcessBlendPixelShader1001110":
                case "FSFXUberPostProcessBlendPixelShader1000100":
                case "FSFXUberPostProcessBlendPixelShader0101101":
                case "FSFXUberPostProcessBlendPixelShader1111101":
                case "FSFXUberPostProcessBlendPixelShader1010101":
                case "FSFXUberPostProcessBlendPixelShader0100111":
                case "FSFXUberPostProcessBlendPixelShader0111001":
                case "FSFXUberPostProcessBlendPixelShader1110111":
                case "FSFXUberPostProcessBlendPixelShader1101001":
                case "FSFXUberPostProcessBlendPixelShader1011111":
                case "FSFXUberPostProcessBlendPixelShader1001011":
                case "FSFXUberPostProcessBlendPixelShader0100110":
                case "FSFXUberPostProcessBlendPixelShader0111000":
                case "FSFXUberPostProcessBlendPixelShader1110110":
                case "FSFXUberPostProcessBlendPixelShader1101000":
                case "FSFXUberPostProcessBlendPixelShader1011110":
                case "FSFXUberPostProcessBlendPixelShader1001010":
                case "FSFXUberPostProcessBlendPixelShader0100101":
                case "FSFXUberPostProcessBlendPixelShader0110100":
                case "FSFXUberPostProcessBlendPixelShader0111011":
                case "FSFXUberPostProcessBlendPixelShader0111110":
                case "FSFXUberPostProcessBlendPixelShader1110101":
                case "FSFXUberPostProcessBlendPixelShader1101110":
                case "FSFXUberPostProcessBlendPixelShader1101011":
                case "FSFXUberPostProcessBlendPixelShader1100100":
                case "FSFXUberPostProcessBlendPixelShader1011101":
                case "FSFXUberPostProcessBlendPixelShader1001001":
                case "FSFXUberPostProcessBlendPixelShader0100100":
                case "FSFXUberPostProcessBlendPixelShader0110101":
                case "FSFXUberPostProcessBlendPixelShader0111010":
                case "FSFXUberPostProcessBlendPixelShader0111111":
                case "FSFXUberPostProcessBlendPixelShader1110100":
                case "FSFXUberPostProcessBlendPixelShader1101111":
                case "FSFXUberPostProcessBlendPixelShader1101010":
                case "FSFXUberPostProcessBlendPixelShader1100101":
                case "FSFXUberPostProcessBlendPixelShader1011100":
                case "FSFXUberPostProcessBlendPixelShader1001000":
                case "FSFXUberPostProcessBlendPixelShader0011000":
                case "FSFXUberPostProcessBlendPixelShader0100001":
                case "FSFXUberPostProcessBlendPixelShader1110001":
                case "FSFXUberPostProcessBlendPixelShader1011001":
                case "FSFXUberPostProcessBlendPixelShader1001101":
                case "FSFXUberPostProcessBlendPixelShader1000111":
                case "FSFXUberPostProcessBlendPixelShader1000010":
                case "FSFXUberPostProcessBlendPixelShader0011001":
                case "FSFXUberPostProcessBlendPixelShader0100000":
                case "FSFXUberPostProcessBlendPixelShader1110000":
                case "FSFXUberPostProcessBlendPixelShader1011000":
                case "FSFXUberPostProcessBlendPixelShader1001100":
                case "FSFXUberPostProcessBlendPixelShader1000110":
                case "FSFXUberPostProcessBlendPixelShader1000011":
                case "FSFXUberPostProcessBlendPixelShader0011010":
                case "FSFXUberPostProcessBlendPixelShader0110111":
                case "FSFXUberPostProcessBlendPixelShader0111101":
                case "FSFXUberPostProcessBlendPixelShader1101101":
                case "FSFXUberPostProcessBlendPixelShader1100111":
                case "FSFXUberPostProcessBlendPixelShader1000000":
                case "FSFXUberPostProcessBlendPixelShader0011011":
                case "FSFXUberPostProcessBlendPixelShader0110110":
                case "FSFXUberPostProcessBlendPixelShader0111100":
                case "FSFXUberPostProcessBlendPixelShader1101100":
                case "FSFXUberPostProcessBlendPixelShader1100110":
                case "FSFXUberPostProcessBlendPixelShader1000001":
                case "FSFXUberPostProcessBlendPixelShader0110001":
                case "FSFXUberPostProcessBlendPixelShader1100001":
                case "FSFXUberPostProcessBlendPixelShader0110000":
                case "FSFXUberPostProcessBlendPixelShader1100000":
                case "FSFXUberPostProcessBlendPixelShader0000100":
                case "FSFXUberPostProcessBlendPixelShader0010101":
                case "FSFXUberPostProcessBlendPixelShader0000101":
                case "FSFXUberPostProcessBlendPixelShader0010100":
                case "FSFXUberPostProcessBlendPixelShader0010011":
                case "FSFXUberPostProcessBlendPixelShader0010010":
                case "FSFXUberPostProcessBlendPixelShader0010001":
                case "FSFXUberPostProcessBlendPixelShader0010000":
                case "FSFXUberPostProcessBlendPixelShader0001111":
                case "FSFXUberPostProcessBlendPixelShader0001110":
                case "FSFXUberPostProcessBlendPixelShader0001101":
                case "FSFXUberPostProcessBlendPixelShader0001100":
                case "FSFXUberPostProcessBlendPixelShader0001011":
                case "FSFXUberPostProcessBlendPixelShader0001010":
                case "FSFXUberPostProcessBlendPixelShader0001001":
                case "FSFXUberPostProcessBlendPixelShader0001000":
                case "FSFXUberPostProcessBlendPixelShader0000111":
                case "FSFXUberPostProcessBlendPixelShader0000110":
                case "FSFXUberPostProcessBlendPixelShader0000011":
                case "FSFXUberPostProcessBlendPixelShader0000010":
                case "FSFXUberPostProcessBlendPixelShader0000001":
                case "FSFXUberPostProcessBlendPixelShader0000000":
                    FUberPostProcessBlendPixelShader();
                    node.Items.Add(FShaderResourceParameter("unk"));
                    node.Items.Add(FShaderParameter("unk"));
                    node.Items.Add(FShaderParameter("unk"));
                    node.Items.Add(FShaderResourceParameter("unk"));
                    node.Items.Add(FShaderParameter("unk"));
                    node.Items.Add(FShaderParameter("unk"));
                    break;
                case "FUberPostProcessVertexShader":
                    node.Items.Add(FShaderParameter("SceneCoordinate1ScaleBiasParameter"));
                    node.Items.Add(FShaderParameter("SceneCoordinate2ScaleBiasParameter"));
                    node.Items.Add(FShaderParameter("SceneCoordinate3ScaleBiasParameter"));
                    break;
                case "TModShadowVolumePixelShaderFSpotLightPolicy":
                    FModShadowVolumePixelShader_Maybe();
                    FSpotLightPolicy_ModShadowPixelParamsType();
                    break;
                case "FSFXUberHalfResPixelShader0001":
                case "FSFXUberHalfResPixelShader0010":
                case "FSFXUberHalfResPixelShader1000":
                case "FSFXUberHalfResPixelShader1011":
                case "FSFXUberHalfResPixelShader0000":
                case "FSFXUberHalfResPixelShader0011":
                case "FSFXUberHalfResPixelShader1001":
                case "FSFXUberHalfResPixelShader1010":
                case "FUberHalfResPixelShader101":
                case "FUberHalfResPixelShader100":
                case "FUberHalfResPixelShader001":
                case "FUberHalfResPixelShader000":
                    FDOFAndBloomBlendPixelShader();
                    node.Items.Add(FMotionBlurShaderParameters("MotionBlurParameters"));
                    node.Items.Add(FShaderResourceParameter("LowResSceneBufferPoint"));
                    break;
                case "FUberPostProcessBlendPixelShader001":
                case "FUberPostProcessBlendPixelShader010":
                case "FUberPostProcessBlendPixelShader100":
                case "FUberPostProcessBlendPixelShader111":
                case "FUberPostProcessBlendPixelShader000":
                case "FUberPostProcessBlendPixelShader011":
                case "FUberPostProcessBlendPixelShader101":
                case "FUberPostProcessBlendPixelShader110":
                    FUberPostProcessBlendPixelShader();
                    break;
                case "TAOApplyPixelShader<AOApply_ReadFromAOHistory>":
                case "TAOApplyPixelShader<AOApply_Normal>":
                    node.Items.Add(FAmbientOcclusionParams("AOParams"));
                    node.Items.Add(FShaderParameter("OcclusionColorParameter"));
                    node.Items.Add(FShaderParameter("FogColorParameter"));
                    node.Items.Add(FShaderParameter("TargetSizeParameter"));
                    node.Items.Add(FShaderParameter("InvEncodePowerParameter"));
                    node.Items.Add(FShaderResourceParameter("FogTextureParameter"));
                    break;
                case "TModShadowProjectionPixelShaderFSpotLightPolicyF16SampleManualPCF":
                case "TModShadowProjectionPixelShaderFSpotLightPolicyF16SampleFetch4PCF":
                case "TModShadowProjectionPixelShaderFSpotLightPolicyF16SampleHwPCF":
                case "TModShadowProjectionPixelShaderFSpotLightPolicyF4SampleManualPCF":
                case "TModShadowProjectionPixelShaderFSpotLightPolicyF4SampleHwPCF":
                    TModShadowProjectionPixelShader();
                    FSpotLightPolicy_ModShadowPixelParamsType();
                    break;
                case "FFluidApplyPixelShader":
                    ;
                    node.Items.Add(FShaderResourceParameter("FluidHeightTextureParameter"));
                    node.Items.Add(FShaderResourceParameter("FluidNormalTextureParameter"));
                    break;
                case "TModShadowProjectionPixelShaderFDirectionalLightPolicyF16SampleManualPCF":
                case "TModShadowProjectionPixelShaderFDirectionalLightPolicyF16SampleFetch4PCF":
                case "TModShadowProjectionPixelShaderFDirectionalLightPolicyF16SampleHwPCF":
                case "TModShadowProjectionPixelShaderFDirectionalLightPolicyF4SampleManualPCF":
                case "TModShadowProjectionPixelShaderFDirectionalLightPolicyF4SampleHwPCF":
                    TModShadowProjectionPixelShader();
                    //FDirectionalLightPolicy::ModShadowPixelParamsType has no params
                    break;
                case "FXAAFilterComputeShaderVerticalDebug":
                case "FXAAFilterComputeShaderVertical":
                case "FXAAFilterComputeShaderHorizontalDebug":
                case "FXAAFilterComputeShaderHorizontal":
                    node.Items.Add(FShaderResourceParameter("unk"));
                    node.Items.Add(FShaderResourceParameter("unk"));
                    node.Items.Add(FShaderResourceParameter("unk"));
                    node.Items.Add(FShaderResourceParameter("unk"));
                    node.Items.Add(FShaderResourceParameter("unk"));
                    node.Items.Add(FShaderParameter("unk"));
                    break;
                case "TShadowProjectionPixelShader<F16SampleManualPCF>":
                case "TShadowProjectionPixelShader<F16SampleFetch4PCF>":
                case "TShadowProjectionPixelShader<F16SampleHwPCF>":
                case "TShadowProjectionPixelShader<F4SampleManualPCF>":
                case "TShadowProjectionPixelShader<F4SampleHwPCF>":
                    node.Items.Add(FSceneTextureShaderParameters("SceneTextureParameters"));
                    node.Items.Add(FShaderParameter("VelocityScaleOffset?"));
                    node.Items.Add(FShaderResourceParameter("ShadowDepthTextureParameter?"));
                    node.Items.Add(FShaderResourceParameter("unk"));
                    node.Items.Add(FShaderParameter("SampleOffsetsParameter?"));
                    node.Items.Add(FShaderParameter("ShadowBufferSizeAndSoftTransitionScaleParameter?"));
                    node.Items.Add(FShaderParameter("ShadowFadeFractionParameter?"));
                    break;
                case "FBlurLightShaftsPixelShader":
                    node.Items.Add(FLightShaftPixelShaderParameters("LightShaftParameters"));
                    node.Items.Add(FShaderParameter("BlurPassIndexParameter"));
                    break;
                case "FFilterVSMComputeShader":
                    node.Items.Add(FShaderResourceParameter("DepthTexture"));
                    node.Items.Add(FShaderResourceParameter("VarianceTexture"));
                    node.Items.Add(FShaderParameter("SubRect"));
                    break;
                case "FDistortionApplyScreenPixelShader":
                    node.Items.Add(FShaderResourceParameter("AccumulatedDistortionTextureParam"));
                    node.Items.Add(FShaderResourceParameter("SceneColorTextureParam"));
                    node.Items.Add(FShaderParameter("SceneColorRectParameter"));
                    break;
                case "FSRGBMLAABlendPixelShader":
                    node.Items.Add(FSceneTextureShaderParameters("SceneTextureParameters"));
                    node.Items.Add(FShaderResourceParameter("unk"));
                    node.Items.Add(FShaderParameter("unk"));
                    node.Items.Add(FShaderParameter("unk"));
                    node.Items.Add(FShaderParameter("unk"));
                    break;
                case "TBasePassVertexShaderFNoLightMapPolicyFNoDensityPolicy":
                    //FNoLightMapPolicy::VertexParametersType has no params
                    node.Items.Add(FVertexFactoryParameterRef());
                    node.Items.Add(FHeightFogVertexShaderParameters("HeightFogParameters"));
                    node.Items.Add(FMaterialVertexShaderParameters("MaterialParameters"));
                    //FNoDensityPolicy::VertexShaderParametersType has no params
                    break;
                case "FShadowProjectionMaskPixelShader":
                    node.Items.Add(FShaderParameter("unk"));
                    node.Items.Add(FShaderParameter("unk"));
                    node.Items.Add(FShaderResourceParameter("unk"));
                    break;
                case "TModShadowProjectionPixelShaderFPointLightPolicyF16SampleManualPCF":
                case "TModShadowProjectionPixelShaderFPointLightPolicyF16SampleFetch4PCF":
                case "TModShadowProjectionPixelShaderFPointLightPolicyF16SampleHwPCF":
                case "TModShadowProjectionPixelShaderFPointLightPolicyF4SampleManualPCF":
                case "TModShadowProjectionPixelShaderFPointLightPolicyF4SampleHwPCF":
                    TModShadowProjectionPixelShader();
                    FPointLightPolicy_ModShadowPixelParamsType();
                    break;
                case "FShaderComplexityApplyPixelShader":
                    node.Items.Add(FSceneTextureShaderParameters("SceneTextureParameters"));
                    node.Items.Add(FShaderParameter("ShaderComplexityColorsParameter"));
                    break;
                case "FCopyTranslucencyDepthPixelShader":
                case "TDownsampleDepthPixelShaderTRUE":
                case "TDownsampleDepthPixelShaderFALSE":
                    node.Items.Add(FSceneTextureShaderParameters("SceneTextureParameters"));
                    break;
                case "FApplyLightShaftsPixelShader":
                    node.Items.Add(FLightShaftPixelShaderParameters("LightShaftParameters"));
                    node.Items.Add(FSceneTextureShaderParameters("SceneTextureParameters"));
                    node.Items.Add(FShaderResourceParameter("SmallSceneColorTextureParameter"));
                    break;
                case "FFXAAResolveComputeShader":
                    node.Items.Add(FShaderResourceParameter("unk"));
                    node.Items.Add(FShaderResourceParameter("unk"));
                    node.Items.Add(FShaderResourceParameter("unk"));
                    break;
                case "FDownsampleSceneDepthAndNormalsPixelShader":
                    node.Items.Add(FShaderParameter("ProjectionScaleBias"));
                    node.Items.Add(FShaderParameter("SourceTexelOffsets01"));
                    node.Items.Add(FShaderParameter("SourceTexelOffsets23"));
                    node.Items.Add(FSceneTextureShaderParameters("SceneTextureParameters"));
                    node.Items.Add(FShaderResourceParameter("FullSizedNormalsTexture"));
                    node.Items.Add(FShaderParameter("OffsetIndex"));
                    break;
                case "FFXAAPrepComputeShader":
                    node.Items.Add(FShaderResourceParameter("unk"));
                    node.Items.Add(FShaderResourceParameter("unk"));
                    node.Items.Add(FShaderResourceParameter("unk"));
                    node.Items.Add(FShaderResourceParameter("unk"));
                    node.Items.Add(FShaderResourceParameter("unk"));
                    node.Items.Add(FShaderParameter("unk"));
                    node.Items.Add(FShaderParameter("unk"));
                    node.Items.Add(FShaderParameter("unk"));
                    node.Items.Add(FShaderParameter("unk"));
                    break;
                case "Fetch4PCFMediumQualityShaderName":
                case "HwPCFMediumQualityShaderName":
                case "Fetch4PCFHighQualityShaderName":
                case "HwPCFHighQualityShaderName":
                case "Fetch4PCFLowQualityShaderName":
                case "HwPCFLowQualityShaderName":
                case "HighQualityShaderName":
                case "MediumQualityShaderName":
                case "LowQualityShaderName":
                    FBranchingPCFProjectionPixelShader();
                    break;
                case "FSRGBMLAAEdgeDetectionPixelShader":
                    node.Items.Add(FSceneTextureShaderParameters("SceneTextureParameters"));
                    node.Items.Add(FShaderParameter("unk"));
                    node.Items.Add(FShaderParameter("unk"));
                    node.Items.Add(FShaderParameter("unk"));
                    break;
                case "THeightFogVertexShader<4>":
                case "THeightFogVertexShader<1>":
                    node.Items.Add(FShaderParameter("ScreenPositionScaleBias"));
                    node.Items.Add(FShaderParameter("FogMinHeight"));
                    node.Items.Add(FShaderParameter("FogMaxHeight"));
                    node.Items.Add(FShaderParameter("ScreenToWorld"));
                    break;
                case "FApplyLightShaftsVertexShader":
                    node.Items.Add(FShaderParameter("SourceTextureScaleBias"));
                    node.Items.Add(FShaderParameter("SceneColorScaleBias"));
                    break;
                case "TBasePassPixelShaderFSHLightLightMapPolicySkyLight":
                case "TBasePassPixelShaderFSHLightLightMapPolicyNoSkyLight":
                    FSHLightLightMapPolicy_PixelParametersType();
                    TBasePassPixelShader();
                    break;
                case "FLUTBlenderPixelShader<1>":
                    node.Items.Add(FShaderResourceParameter("Texture0"));
                    goto case "FLUTBlenderPixelShader<2>";
                case "FLUTBlenderPixelShader<2>":
                    node.Items.Add(FShaderResourceParameter("Texture1"));
                    goto case "FLUTBlenderPixelShader<3>";
                case "FLUTBlenderPixelShader<3>":
                    node.Items.Add(FShaderResourceParameter("Texture2"));
                    goto case "FLUTBlenderPixelShader<4>";
                case "FLUTBlenderPixelShader<4>":
                    node.Items.Add(FShaderResourceParameter("Texture3"));
                    goto case "FLUTBlenderPixelShader<5>";
                case "FLUTBlenderPixelShader<5>":
                    node.Items.Add(FShaderResourceParameter("Texture4"));
                    node.Items.Add(FShaderParameter("Weights"));
                    node.Items.Add(FGammaShaderParameters("GammaParameters"));
                    node.Items.Add(FColorRemapShaderParameters("MaterialParameters"));
                    break;
                case "FFluidNormalPixelShader":
                    node.Items.Add(FShaderParameter("CellSize"));
                    node.Items.Add(FShaderParameter("HeightScale"));
                    node.Items.Add(FShaderResourceParameter("HeightTexture"));
                    node.Items.Add(FShaderParameter("SplineMarginParameter"));
                    break;
                case "TDownsampleLightShaftsPixelShader<LS_Spot>":
                case "TDownsampleLightShaftsPixelShader<LS_Directional>":
                case "TDownsampleLightShaftsPixelShader<LS_Point>":
                    node.Items.Add(FLightShaftPixelShaderParameters("LightShaftParameters"));
                    node.Items.Add(FShaderParameter("SampleOffsets"));
                    node.Items.Add(FSceneTextureShaderParameters("SceneTextureParameters"));
                    node.Items.Add(FShaderResourceParameter("SmallSceneColorTexture"));
                    break;
                case "FModShadowMeshPixelShader":
                    node.Items.Add(FMaterialPixelShaderParameters("MaterialParameters"));
                    node.Items.Add(FShaderParameter("AttenAllowed")); 
                    break;
                case "FFluidSimulatePixelShader":
                    node.Items.Add(FShaderParameter("CellSize"));
                    node.Items.Add(FShaderParameter("DampFactor"));
                    node.Items.Add(FShaderParameter("TravelSpeed"));
                    node.Items.Add(FShaderParameter("PreviousOffset1"));
                    node.Items.Add(FShaderParameter("PreviousOffset2"));
                    node.Items.Add(FShaderResourceParameter("PreviousHeights1"));
                    node.Items.Add(FShaderResourceParameter("PreviousHeights2"));
                    break;
                case "FApplyForcePixelShader":
                    node.Items.Add(FShaderParameter("ForcePosition"));
                    node.Items.Add(FShaderParameter("ForceRadius"));
                    node.Items.Add(FShaderParameter("ForceMagnitude"));
                    node.Items.Add(FShaderResourceParameter("PreviousHeights1"));
                    break;
                case "FDOFAndBloomBlendPixelShader":
                    FDOFAndBloomBlendPixelShader();
                    break;
                case "TDOFBoxBlurMinPixelShader<3>":
                case "TDOFBoxBlurMaxPixelShader<5>":
                case "TDOFBlur1PixelShader<4>":
                case "TDOFBoxBlurMinPixelShader<2>":
                case "TDOFBoxBlurMaxPixelShader<4>":
                case "TDOFBlur1PixelShader<3>":
                case "TDOFBoxBlurMinPixelShader<5>":
                case "TDOFBoxBlurMaxPixelShader<3>":
                case "TDOFBoxBlurMinPixelShader<4>":
                case "TDOFBoxBlurMaxPixelShader<2>":
                case "TDOFBlur2PixelShader<8>":
                case "TDOFBlur2PixelShader<6>":
                case "TDOFBlur2PixelShader<4>":
                case "TDOFBlur2PixelShader<3>":
                case "TDOFBlur1PixelShader<8>":
                case "TDOFBlur1PixelShader<6>":
                    node.Items.Add(FDOFShaderParameters("DOFParameters"));
                    node.Items.Add(FShaderResourceParameter("DOFTempTexture"));
                    node.Items.Add(FShaderResourceParameter("DOFTempTexture2"));
                    node.Items.Add(FShaderParameter("DOFKernelParams"));
                    node.Items.Add(FShaderParameter("BlurDirections"));
                    break;
                case "TDOFAndBloomGatherPixelShader<MAX_FILTER_SAMPLES>":
                case "TBloomGatherPixelShader<NumFPFilterSamples>":
                case "TDOFAndBloomGatherPixelShader<NumFPFilterSamples>":
                    TDOFAndBloomGatherPixelShader();
                    break;
                case "TLightMapDensityPixelShader<FDirectionalLightMapTexturePolicy>":
                case "TLightMapDensityPixelShader<FDummyLightMapTexturePolicy>":
                case "TLightMapDensityPixelShader<FSimpleLightMapTexturePolicy>":
                    FLightMapTexturePolicy_PixelParametersType();
                    node.Items.Add(FMaterialPixelShaderParameters("MaterialParameters"));
                    node.Items.Add(FShaderParameter("LightMapDensityParameters"));
                    node.Items.Add(FShaderParameter("BuiltLightingAndSelectedFlags"));
                    node.Items.Add(FShaderParameter("DensitySelectedColor"));
                    node.Items.Add(FShaderParameter("LightMapResolutionScale"));
                    node.Items.Add(FShaderParameter("LightMapDensityDisplayOptions"));
                    node.Items.Add(FShaderParameter("VertexMappedColor"));
                    node.Items.Add(FShaderResourceParameter("GridTexture"));
                    break;
                case "TDOFGatherPixelShader<NumFPFilterSamples>":
                    TDOFAndBloomGatherPixelShader();
                    node.Items.Add(FShaderParameter("InputTextureSize"));
                    break;
                case "TModShadowVolumePixelShaderFDirectionalLightPolicy":
                    FModShadowVolumePixelShader_Maybe();
                    //FDirectionalLightPolicy::ModShadowPixelParamsType has no params
                    break;
                case "FHBAOBlurComputeShader":
                    node.Items.Add(FHBAOShaderParameters("HBAOParameters"));
                    node.Items.Add(FShaderResourceParameter("AOTexture"));
                    node.Items.Add(FShaderResourceParameter("BlurOut"));
                    node.Items.Add(FShaderParameter("AOTexDimensions"));
                    break;
                case "FHBAODeinterleaveComputeShader":
                    node.Items.Add(FHBAOShaderParameters("HBAOParameters"));
                    node.Items.Add(FShaderResourceParameter("SceneDepthTexture"));
                    node.Items.Add(FShaderResourceParameter("DeinterleaveOut"));
                    node.Items.Add(FShaderParameter("ArrayOffset"));
                    break;
                case "FFXAA3VertexShader":
                    node.Items.Add(FShaderParameter("rcpFrame"));
                    node.Items.Add(FShaderParameter("rcpFrameOpt"));
                    break;
                case "FSimpleElementDistanceFieldGammaPixelShader":
                    node.Items.Add(FShaderParameter("SmoothWidth"));
                    node.Items.Add(FShaderParameter("EnableShadow"));
                    node.Items.Add(FShaderParameter("ShadowDirection"));
                    node.Items.Add(FShaderParameter("ShadowColor"));
                    node.Items.Add(FShaderParameter("ShadowSmoothWidth"));
                    node.Items.Add(FShaderParameter("EnableGlow"));
                    node.Items.Add(FShaderParameter("GlowColor"));
                    node.Items.Add(FShaderParameter("GlowOuterRadius"));
                    node.Items.Add(FShaderParameter("GlowInnerRadius"));
                    break;
                case "TShadowDepthVertexShader<ShadowDepth_OutputDepthToColor>":
                case "TShadowDepthVertexShader<ShadowDepth_PerspectiveCorrect>":
                case "TShadowDepthVertexShader<ShadowDepth_OutputDepth>":
                    node.Items.Add(FVertexFactoryParameterRef());
                    node.Items.Add(FMaterialVertexShaderParameters("MaterialParameters"));
                    node.Items.Add(FShaderParameter("ProjectionMatrix"));
                    node.Items.Add(FShaderParameter("InvMaxSubjectDepth"));
                    node.Items.Add(FShaderParameter("DepthBias"));
                    node.Items.Add(FShaderParameter("bClampToNearPlane"));
                    break;
                case "FSimpleElementMaskedGammaPixelShader":
                    FSimpleElementGammaPixelShader();
                    node.Items.Add(FShaderParameter("ClipRef"));
                    break;
                case "FSimpleElementGammaPixelShader":
                    FSimpleElementGammaPixelShader();
                    break;
                case "FGenerateDeinterleavedHBAOComputeShader":
                    node.Items.Add(FHBAOShaderParameters("HBAOParameters"));
                    node.Items.Add(FShaderResourceParameter("OutAO"));
                    node.Items.Add(FShaderResourceParameter("QuarterResDepthCS"));
                    node.Items.Add(FShaderResourceParameter("ViewNormalTex"));
                    node.Items.Add(FShaderParameter("JitterCS"));
                    node.Items.Add(FShaderParameter("ArrayOffset"));
                    break;
                case "FHBAOReconstructNormalsComputeShader":
                    node.Items.Add(FHBAOShaderParameters("HBAOParameters"));
                    node.Items.Add(FShaderResourceParameter("SceneDepthTexture"));
                    node.Items.Add(FShaderResourceParameter("ReconstructNormalOut"));
                    break;
                case "TAOMaskPixelShader<AO_HistoryUpdateManualDepthTest>":
                case "TAOMaskPixelShader<AO_HistoryMaskManualDepthTest>":
                case "TAOMaskPixelShader<AO_HistoryUpdate>":
                case "TAOMaskPixelShader<AO_HistoryMask>":
                case "TAOMaskPixelShader<AO_OcclusionMask>":
                    node.Items.Add(FAmbientOcclusionParams("AOParams"));
                    node.Items.Add(FShaderParameter("HistoryConvergenceRates"));
                    break;
                case "FStaticHistoryUpdatePixelShader":
                    node.Items.Add(FAmbientOcclusionParams("AOParams"));
                    node.Items.Add(FShaderParameter("PrevViewProjMatrix"));
                    node.Items.Add(FShaderParameter("HistoryConvergenceRates"));
                    break;
                case "TEdgePreservingFilterPixelShader<30>":
                case "TEdgePreservingFilterPixelShader<20>":
                case "TEdgePreservingFilterPixelShader<28>":
                case "TEdgePreservingFilterPixelShader<26>":
                case "TEdgePreservingFilterPixelShader<24>":
                case "TEdgePreservingFilterPixelShader<22>":
                case "TEdgePreservingFilterPixelShader<1>":
                case "TEdgePreservingFilterPixelShader<18>":
                case "TEdgePreservingFilterPixelShader<16>":
                case "TEdgePreservingFilterPixelShader<14>":
                case "TEdgePreservingFilterPixelShader<12>":
                case "TEdgePreservingFilterPixelShader<10>":
                case "TEdgePreservingFilterPixelShader<8>":
                case "TEdgePreservingFilterPixelShader<6>":
                case "TEdgePreservingFilterPixelShader<4>":
                case "TEdgePreservingFilterPixelShader<2>":
                    node.Items.Add(FAmbientOcclusionParams("AOParams"));
                    node.Items.Add(FShaderParameter("FilterSampleOffsets"));
                    node.Items.Add(FShaderParameter("FilterParameters"));
                    node.Items.Add(FShaderParameter("CustomParameters"));
                    break;
                case "TAmbientOcclusionPixelShaderFDefaultQualityAOTRUEFALSE":
                case "TAmbientOcclusionPixelShaderFDefaultQualityAOFALSETRUE":
                case "TAmbientOcclusionPixelShaderFDefaultQualityAOFALSEFALSE":
                case "TAmbientOcclusionPixelShaderFDefaultQualityAOTRUETRUE":
                    node.Items.Add(FShaderParameter("OcclusionSampleOffsets"));
                    node.Items.Add(FShaderResourceParameter("RandomNormalTexture"));
                    node.Items.Add(FShaderParameter("ProjectionScale"));
                    node.Items.Add(FShaderParameter("ProjectionMatrix"));
                    node.Items.Add(FShaderParameter("NoiseScale"));
                    node.Items.Add(FAmbientOcclusionParams("AOParams"));
                    node.Items.Add(FShaderParameter("OcclusionCalcParameters"));
                    node.Items.Add(FShaderParameter("HaloDistanceScale"));
                    node.Items.Add(FShaderParameter("OcclusionRemapParameters"));
                    node.Items.Add(FShaderParameter("OcclusionFadeoutParameters"));
                    node.Items.Add(FShaderParameter("MaxRadiusTransform"));
                    break;
                case "FBinkGpuShaderYCrCbToRGBNoAlpha":
                case "FBinkGpuShaderYCrCbToRGB":
                    node.Items.Add(FShaderResourceParameter("YTex"));
                    node.Items.Add(FShaderResourceParameter("CrCbTex"));
                    node.Items.Add(FShaderResourceParameter("ATex"));
                    node.Items.Add(FShaderParameter("cmatrix"));
                    node.Items.Add(FShaderParameter("alpha_mult"));
                    break;
                case "FBinkGpuShaderHDRNoAlpha":
                case "FBinkGpuShaderHDR":
                    node.Items.Add(FShaderResourceParameter("YTex"));
                    node.Items.Add(FShaderResourceParameter("CrCbTex"));
                    node.Items.Add(FShaderResourceParameter("ATex"));
                    node.Items.Add(FShaderResourceParameter("HTex"));
                    node.Items.Add(FShaderParameter("alpha_mult"));
                    node.Items.Add(FShaderParameter("hdr"));
                    node.Items.Add(FShaderParameter("ctcp"));
                    break;
                case "FBinkYCrCbAToRGBAPixelShader":
                    Debugger.Break(); //FShader serialization AFTER params?
                    node.Items.Add(FShaderResourceParameter("tex3"));
                    //TODO: check if this also serializes FBinkYCrCbToRGBNoPixelAlphaPixelShader params
                    break; 
                case "FBinkYCrCbToRGBNoPixelAlphaPixelShader":
                    Debugger.Break(); //FShader serialization AFTER params?
                    node.Items.Add(FShaderResourceParameter("tex0"));
                    node.Items.Add(FShaderResourceParameter("tex1"));
                    node.Items.Add(FShaderResourceParameter("tex2"));
                    node.Items.Add(FShaderParameter("crc"));
                    node.Items.Add(FShaderParameter("cbc"));
                    node.Items.Add(FShaderParameter("adj"));
                    node.Items.Add(FShaderParameter("yscale"));
                    node.Items.Add(FShaderParameter("consts"));
                    break;
                case "TLightPixelShaderFSpotLightPolicyFNoStaticShadowingPolicy":
                case "TLightPixelShaderFSpotLightPolicyFShadowVertexBufferPolicy":
                    FSpotLightPolicy_PixelParametersType();
                    TLightPixelShader();
                    break;
                case "TLightMapDensityVertexShader<FDummyLightMapTexturePolicy>":
                case "TLightMapDensityVertexShader<FDirectionalLightMapTexturePolicy>":
                case "TLightMapDensityVertexShader<FSimpleLightMapTexturePolicy>":
                    FLightMapTexturePolicy_VertexParametersType();
                    node.Items.Add(FVertexFactoryParameterRef());
                    node.Items.Add(FMaterialVertexShaderParameters("MaterialParameters"));
                    break;
                case "TLightVertexShaderFDirectionalLightPolicyFNoStaticShadowingPolicy":
                case "TLightVertexShaderFDirectionalLightPolicyFShadowVertexBufferPolicy":
                    FDirectionalLightPolicy_VertexParametersType();
                    node.Items.Add(FVertexFactoryParameterRef());
                    node.Items.Add(FMaterialVertexShaderParameters("MaterialParameters"));
                    break;
                case "TLightVertexShaderFSpotLightPolicyFNoStaticShadowingPolicy":
                case "TLightVertexShaderFSpotLightPolicyFShadowVertexBufferPolicy":
                case "TLightVertexShaderFPointLightPolicyFNoStaticShadowingPolicy":
                case "TLightVertexShaderFPointLightPolicyFShadowVertexBufferPolicy":
                    FPointLightPolicy_VertexParametersType();
                    node.Items.Add(FVertexFactoryParameterRef());
                    node.Items.Add(FMaterialVertexShaderParameters("MaterialParameters"));
                    break;
                case "TLightPixelShaderFSphericalHarmonicLightPolicyFNoStaticShadowingPolicy":
                    FSphericalHarmonicLightPolicy_PixelParametersType();
                    TLightPixelShader();
                    break;
                case "TLightPixelShaderFPointLightPolicyFNoStaticShadowingPolicy":
                case "TLightPixelShaderFPointLightPolicyFShadowVertexBufferPolicy":
                    FPointLightPolicy_PixelParametersType();
                    TLightPixelShader();
                    break;
                case "TLightVertexShaderFSphericalHarmonicLightPolicyFNoStaticShadowingPolicy":
                    node.Items.Add(FVertexFactoryParameterRef());
                    node.Items.Add(FMaterialVertexShaderParameters("MaterialParameters"));
                    break;
                case "FModShadowMeshVertexShader":
                    node.Items.Add(FVertexFactoryParameterRef());
                    node.Items.Add(FMaterialVertexShaderParameters("MaterialParameters"));
                    node.Items.Add(FShaderParameter("LightPosition"));
                    break;
                case "TLightPixelShaderFDirectionalLightPolicyFNoStaticShadowingPolicy":
                case "TLightPixelShaderFDirectionalLightPolicyFShadowVertexBufferPolicy":
                    FDirectionalLightPolicy_PixelParametersType();
                    TLightPixelShader();
                    break;
                case "FSFXWorldNormalPixelShader":
                case "TDepthOnlyScreenDoorPixelShader":
                case "FTranslucencyPostRenderDepthPixelShader":
                case "TDistortionMeshPixelShader<FDistortMeshAccumulatePolicy>":
                case "TDepthOnlySolidPixelShader":
                    node.Items.Add(FMaterialPixelShaderParameters("MaterialParameters"));
                    break;
                case "FSFXWorldNormalVertexShader":
                case "TLightMapDensityVertexShader<FNoLightMapPolicy>":
                case "FTextureDensityVertexShader":
                case "TDepthOnlyVertexShader<0>":
                case "FHitProxyVertexShader":
                case "TDistortionMeshVertexShader<FDistortMeshAccumulatePolicy>":
                case "FFogVolumeApplyVertexShader":
                case "TDepthOnlyVertexShader<1>":
                    node.Items.Add(FVertexFactoryParameterRef());
                    node.Items.Add(FMaterialVertexShaderParameters("MaterialParameters"));
                    break;
                case "TLightPixelShaderFSFXPointLightPolicyFNoStaticShadowingPolicy":
                    FSFXPointLightPolicy_PixelParametersType();
                    TLightPixelShader();
                    break;
                case "TLightVertexShaderFSFXPointLightPolicyFNoStaticShadowingPolicy":
                    FSFXPointLightPolicy_VertexParametersType();
                    node.Items.Add(FVertexFactoryParameterRef());
                    node.Items.Add(FMaterialVertexShaderParameters("MaterialParameters"));
                    break;
                case "TBasePassVertexShaderFSimpleVertexLightMapPolicyFNoDensityPolicy":
                case "TBasePassVertexShaderFDirectionalVertexLightMapPolicyFNoDensityPolicy":
                case "TBasePassVertexShaderFCustomVectorVertexLightMapPolicyFNoDensityPolicy":
                case "TBasePassVertexShaderFCustomSimpleVertexLightMapPolicyFNoDensityPolicy":
                    FVertexLightMapPolicy_VertexParametersType();
                    TBasePassVertexShader();
                    //FNoDensityPolicy::VertexShaderParametersType has no params
                    break;
                case "TBasePassVertexShaderFCustomVectorLightMapTexturePolicyFNoDensityPolicy":
                case "TBasePassVertexShaderFSimpleLightMapTexturePolicyFNoDensityPolicy":
                case "TBasePassVertexShaderFDirectionalLightMapTexturePolicyFNoDensityPolicy":
                case "TBasePassVertexShaderFCustomSimpleLightMapTexturePolicyFNoDensityPolicy":
                    FLightMapTexturePolicy_VertexParametersType();
                    TBasePassVertexShader();
                    //FNoDensityPolicy::VertexShaderParametersType has no params
                    break;
                case "TBasePassVertexShaderFSHLightLightMapPolicyFNoDensityPolicy":
                case "TBasePassVertexShaderFDirectionalLightLightMapPolicyFNoDensityPolicy":
                    FDirectionalLightLightMapPolicy_VertexParametersType();
                    TBasePassVertexShader();
                    //FNoDensityPolicy::VertexShaderParametersType has no params
                    break;
                case "TBasePassPixelShaderFDirectionalLightLightMapPolicySkyLight":
                case "TBasePassPixelShaderFDirectionalLightLightMapPolicyNoSkyLight":
                    FDirectionalLightLightMapPolicy_PixelParametersType();
                    TBasePassPixelShader();
                    break;
                case "TBasePassPixelShaderFNoLightMapPolicySkyLight":
                case "TBasePassPixelShaderFNoLightMapPolicyNoSkyLight":
                case "TBasePassPixelShaderFCustomSimpleVertexLightMapPolicySkyLight":
                case "TBasePassPixelShaderFCustomSimpleVertexLightMapPolicyNoSkyLight":
                case "TBasePassPixelShaderFCustomVectorVertexLightMapPolicySkyLight":
                case "TBasePassPixelShaderFCustomVectorVertexLightMapPolicyNoSkyLight":
                case "TBasePassPixelShaderFSimpleVertexLightMapPolicySkyLight":
                case "TBasePassPixelShaderFSimpleVertexLightMapPolicyNoSkyLight":
                case "TBasePassPixelShaderFDirectionalVertexLightMapPolicySkyLight":
                case "TBasePassPixelShaderFDirectionalVertexLightMapPolicyNoSkyLight":
                    //PixelParametersType for these LightMapPolicys have no params
                    TBasePassPixelShader();
                    break;
                default:
                    Debugger.Break();
                    node = null;
                    break;
            }
        }
        catch (Exception e)
        {
            exception = e;
            node.Items.Add(new BinInterpNode { Header = $"Error reading binary data: {e}" });
        }

        return node;

        BinInterpNode FShaderParameter(string name)
        {
            return new BinInterpNode(bin.Position, $"{name}: FShaderParameter")
            {
                Items =
                {
                    MakeUInt16Node(bin, "BaseIndex"),
                    MakeUInt16Node(bin, "NumBytes"),
                    MakeUInt16Node(bin, "BufferIndex"),
                },
                Length = 6
            };
        }

        BinInterpNode FShaderResourceParameter(string name)
        {
            return new BinInterpNode(bin.Position, $"{name}: FShaderResourceParameter")
            {
                Items =
                {
                    MakeUInt16Node(bin, "BaseIndex"),
                    MakeUInt16Node(bin, "NumResources"),
                    MakeUInt16Node(bin, "SamplerIndex"),
                },
                Length = 6
            };
        }

        BinInterpNode FVertexFactoryParameterRef()
        {
            var vertexFactoryParameterRef = new BinInterpNode(bin.Position, $"VertexFactoryParameters: FVertexFactoryParameterRef")
            {
                Items =
                {
                    MakeNameNode(bin, "VertexFactoryType", out var vertexFactoryName),
                    MakeUInt32HexNode(bin, "File offset to end of FVertexFactoryParameterRef (may be inaccurate in modded files)")
                },
                IsExpanded = true
            };
            vertexFactoryParameterRef.Items.AddRange(FVertexFactoryShaderParameters(vertexFactoryName));
            return vertexFactoryParameterRef;
        }

        BinInterpNode FSceneTextureShaderParameters(string name)
        {
            return new BinInterpNode(bin.Position, $"{name}: FSceneTextureShaderParameters")
            {
                Items =
                {
                    FShaderResourceParameter("SceneColorTextureParameter"),
                    FShaderResourceParameter("SceneDepthTextureParameter"),
                    FShaderParameter("MinZ_MaxZRatio"),
                    FShaderParameter("ScreenPositionScaleBiasParameter"),
                },
            };
        }

        BinInterpNode FMaterialShaderParameters(string name, string type = "FMaterialShaderParameters")
        {
            return new BinInterpNode(bin.Position, $"{name}: {type}")
            {
                Items =
                {
                    FShaderParameter("CameraWorldPositionParameter"),
                    FShaderParameter("ObjectWorldPositionAndRadiusParameter"),
                    FShaderParameter("ObjectOrientationParameter"),
                    FShaderParameter("WindDirectionAndSpeedParameter"),
                    FShaderParameter("FoliageImpulseDirectionParameter"),
                    FShaderParameter("FoliageNormalizedRotationAxisAndAngleParameter"),
                }
            };
        }

        BinInterpNode FMaterialPixelShaderParameters(string name)
        {
            var super = FMaterialShaderParameters(name, "FMaterialPixelShaderParameters");
            super.Items.AddRange(
            [
                MakeArrayNode(bin, "UniformPixelScalarShaderParameters", _ => TUniformParameter(FShaderParameter)),
                MakeArrayNode(bin, "UniformPixelVectorShaderParameters", _ => TUniformParameter(FShaderParameter)),
                MakeArrayNode(bin, "UniformPixel2DShaderResourceParameters", _ => TUniformParameter(FShaderResourceParameter)),
                MakeArrayNode(bin, "UniformPixelCubeShaderResourceParameters", _ => TUniformParameter(FShaderResourceParameter)),
                FShaderParameter("LocalToWorldParameter"),
                FShaderParameter("WorldToLocalParameter"),
                FShaderParameter("WorldToViewParameter"),
                FShaderParameter("InvViewProjectionParameter"),
                FShaderParameter("ViewProjectionParameter"),
                FSceneTextureShaderParameters("SceneTextureParameters"),
                FShaderParameter("TwoSidedSignParameter"),
                FShaderParameter("InvGammaParameter"),
                FShaderParameter("DecalFarPlaneDistanceParameter"),
                FShaderParameter("ObjectPostProjectionPositionParameter"),
                FShaderParameter("ObjectMacroUVScalesParameter"),
                FShaderParameter("ObjectNDCPositionParameter"),
                FShaderParameter("OcclusionPercentageParameter"),
                FShaderParameter("EnableScreenDoorFadeParameter"),
                FShaderParameter("ScreenDoorFadeSettingsParameter"),
                FShaderParameter("ScreenDoorFadeSettings2Parameter"),
                FShaderResourceParameter("ScreenDoorNoiseTextureParameter"),
                MakeUInt32Node(bin, "unk_4bytes"),
                MakeUInt32Node(bin, "unk_4bytes"),
                FShaderParameter("WrapLightingParameters")
            ]);
            return super;
        }

        BinInterpNode FMaterialVertexShaderParameters(string name)
        {
            var super = FMaterialShaderParameters(name, "FMaterialVertexShaderParameters");
            super.Items.AddRange(
            [
                MakeArrayNode(bin, "UniformVertexScalarShaderParameters", _ => TUniformParameter(FShaderParameter)),
                MakeArrayNode(bin, "UniformVertexVectorShaderParameters", _ => TUniformParameter(FShaderParameter)),
            ]);
            return super;
        }

        BinInterpNode TUniformParameter(Func<string, BinInterpNode> parameter)
        {
            return parameter($"[{bin.ReadInt32()}]");
        }

        IEnumerable<ITreeItem> FVertexFactoryShaderParameters(string vertexFactor)
        {
            switch (vertexFactor)
            {
                case "FLocalVertexFactory":
                case "FLocalVertexFactoryApex":
                    return
                    [
                        FShaderParameter("LocalToWorldParameter"),
                        FShaderParameter("LocalToWorldRotDeterminantFlipParameter"),
                        FShaderParameter("WorldToLocalParameter"),
                    ];
                case "FFluidTessellationVertexFactory":
                    return
                    [
                        .. FVertexFactoryShaderParameters("FLocalVertexFactory"),
                        FShaderParameter("GridSizeParameter"),
                        FShaderParameter("TessellationParameters"),
                        FShaderResourceParameter("HeightmapParameter"),
                        FShaderParameter("TessellationFactors1"),
                        FShaderParameter("TessellationFactors2"),
                        FShaderParameter("TexcoordScaleBias"),
                        FShaderParameter("SplineParameters"),
                    ];
                case "FFoliageVertexFactory":
                    return
                    [
                        FShaderParameter("InvNumVerticesPerInstance"),
                        FShaderParameter("NumVerticesPerInstance"),
                    ];
                case "FGPUSkinMorphDecalVertexFactory":
                case "FGPUSkinDecalVertexFactory":

                    return
                    [
                        .. FVertexFactoryShaderParameters("FGPUSkinVertexFactory"),
                        FShaderParameter("BoneToDecalRow0"),
                        FShaderParameter("BoneToDecalRow1"),
                        FShaderParameter("DecalLocation"),
                    ];
                case "FGPUSkinVertexFactory":
                case "FGPUSkinMorphVertexFactory":
                    return
                    [
                        FShaderParameter("LocalToWorld"),
                        FShaderParameter("WorldToLocal"),
                        FShaderParameter("BoneMatrices"),
                        FShaderParameter("MaxBoneInfluences"),
                        FShaderParameter("MeshOrigin"),
                        FShaderParameter("MeshExtension"),
                        FShaderParameter("WoundEllipse0"),
                        FShaderParameter("WoundEllipse1"),
                    ];
                case "FInstancedStaticMeshVertexFactory":
                    return
                    [
                        .. FVertexFactoryShaderParameters("FLocalVertexFactory"),
                        FShaderParameter("InstancedViewTranslation"),
                        FShaderParameter("InstancingParameters"),
                    ];
                case "FLensFlareVertexFactory":
                    return
                    [
                        FShaderParameter("CameraRight"),
                        FShaderParameter("CameraUp"),
                        FShaderParameter("LocalToWorld"),
                    ];
                case "FLocalDecalVertexFactory":
                    return
                    [
                        .. FVertexFactoryShaderParameters("FLocalVertexFactory"),
                        FShaderParameter("DecalMatrix"),
                        FShaderParameter("DecalLocation"),
                        FShaderParameter("DecalOffset"),
                        FShaderParameter("DecalLocalBinormal"),
                        FShaderParameter("DecalLocalTangent"),
                        FShaderParameter("DecalLocalNormal"),
                        FShaderParameter("DecalBlendInterval"),
                    ];
                case "FGPUSkinVertexFactoryApex":
                    return
                    [
                        .. FVertexFactoryShaderParameters("FLocalVertexFactory"),
                        FShaderParameter("BoneMatrices"),
                        FShaderParameter("ApexDummy"),
                    ];
                case "FParticleBeamTrailVertexFactory":
                case "FParticleBeamTrailDynamicParameterVertexFactory":
                    return
                    [
                        FShaderParameter("CameraWorldPositionParameter"),
                        FShaderParameter("CameraRightParameter"),
                        FShaderParameter("CameraUpParameter"),
                        FShaderParameter("ScreenAlignmentParameter"),
                        FShaderParameter("LocalToWorldParameter"),
                    ];
                case "FParticleInstancedMeshVertexFactory":
                    return
                    [
                        FShaderParameter("InvNumVerticesPerInstance"),
                        FShaderParameter("NumVerticesPerInstance"),
                        FShaderParameter("InstancedPreViewTranslation"),
                    ];
                case "FParticleVertexFactory":
                case "FParticleSubUVVertexFactory":
                case "FParticleDynamicParameterVertexFactory":
                case "FParticleSubUVDynamicParameterVertexFactory":
                    return
                    [
                        FShaderParameter("CameraWorldPositionParameter"),
                        FShaderParameter("CameraRightParameter"),
                        FShaderParameter("CameraUpParameter"),
                        FShaderParameter("ScreenAlignmentParameter"),
                        FShaderParameter("LocalToWorldParameter"),
                        FShaderParameter("AxisRotationVectorSourceIndexParameter"),
                        FShaderParameter("AxisRotationVectors"),
                        FShaderParameter("ParticleUpRightResultScalarsParameter"),
                        FShaderParameter("NormalsTypeParameter"),
                        FShaderParameter("NormalsSphereCenterParameter"),
                        FShaderParameter("NormalsCylinderUnitDirectionParameter"),
                    ];
                case "FSplineMeshVertexFactory":
                    return
                    [
                        .. FVertexFactoryShaderParameters("FLocalVertexFactory"),
                        FShaderParameter("SplineStartPos"),
                        FShaderParameter("SplineStartTangent"),
                        FShaderParameter("SplineStartRoll"),
                        FShaderParameter("SplineStartScale"),
                        FShaderParameter("SplineStartOffset"),
                        FShaderParameter("SplineEndPos"),
                        FShaderParameter("SplineEndTangent"),
                        FShaderParameter("SplineEndRoll"),
                        FShaderParameter("SplineEndScale"),
                        FShaderParameter("SplineEndOffset"),
                        FShaderParameter("SplineXDir"),
                        FShaderParameter("SmoothInterpRollScale"),
                        FShaderParameter("MeshMinZ"),
                        FShaderParameter("MeshRangeZ"),
                    ];
                case "FTerrainFullMorphDecalVertexFactory":
                case "FTerrainMorphDecalVertexFactory":
                case "FTerrainDecalVertexFactory":
                    return
                    [
                        .. FVertexFactoryShaderParameters("FTerrainVertexFactory"),
                        FShaderParameter("DecalMatrix"),
                        FShaderParameter("DecalLocation"),
                        FShaderParameter("DecalOffset"),
                        FShaderParameter("DecalLocalBinormal"),
                        FShaderParameter("DecalLocalTangent"),
                    ];
                case "FTerrainFullMorphVertexFactory":
                case "FTerrainMorphVertexFactory":
                case "FTerrainVertexFactory":
                    return
                    [
                        FShaderParameter("LocalToWorld"),
                        FShaderParameter("WorldToLocal"),
                        FShaderParameter("LocalToView"),
                        FShaderParameter("TerrainLightmapCoordinateScaleBias"),
                        FShaderParameter("TessellationInterpolation"),
                        FShaderParameter("InvMaxTesselationLevel_ZScale"),
                        FShaderParameter("InvTerrainSize_SectionBase"),
                        FShaderParameter("Unused"),
                        FShaderParameter("TessellationDistanceScale"),
                        FShaderParameter("TessInterpDistanceValues"),
                    ];
            }
            Debugger.Break();
            return Array.Empty<BinInterpNode>();
        }

        BinInterpNode FGammaShaderParameters(string name)
        {
            return new BinInterpNode(bin.Position, $"{name}: FGammaShaderParameters")
            {
                Items =
                {
                    FShaderParameter("GammaColorScaleAndInverse"),
                    FShaderParameter("GammaOverlayColor"),
                    FShaderResourceParameter("unk"),
                    FShaderParameter("RenderTargetExtent?"),
                },
            };
        }

        BinInterpNode FColorRemapShaderParameters(string name)
        {
            return new BinInterpNode(bin.Position, $"{name}: FColorRemapShaderParameters")
            {
                Items =
                {
                    FShaderParameter("ShadowsAndDesaturationParameter"),
                    FShaderParameter("InverseHighLightsParameter"),
                    FShaderParameter("MidTonesParameter"),
                    FShaderParameter("ScaledLuminanceWeightsParameter"),
                },
            };
        }

        BinInterpNode FAmbientOcclusionParams(string name)
        {
            return new BinInterpNode(bin.Position, $"{name}: FAmbientOcclusionParams")
            {
                Items =
                {
                    FShaderResourceParameter("AmbientOcclusionTextureParameter"),
                    FShaderResourceParameter("AOHistoryTextureParameter"),
                    FShaderParameter("AOScreenPositionScaleBiasParameter"),
                    FShaderParameter("ScreenEdgeLimitsParameter"),
                },
            };
        }

        BinInterpNode FLightShaftPixelShaderParameters(string name)
        {
            return new BinInterpNode(bin.Position, $"{name}: FLightShaftPixelShaderParameters")
            {
                Items =
                {
                    FShaderParameter("TextureSpaceBlurOriginParameter"),
                    FShaderParameter("WorldSpaceBlurOriginAndRadiusParameter"),
                    FShaderParameter("SpotAnglesParameter"),
                    FShaderParameter("WorldSpaceSpotDirectionParameter"),
                    FShaderParameter("WorldSpaceCameraPositionParameter"),
                    FShaderParameter("UVMinMaxParameter"),
                    FShaderParameter("AspectRatioAndInvAspectRatioParameter"),
                    FShaderParameter("LightShaftParameters"),
                    FShaderParameter("BloomTintAndThresholdParameter"),
                    FShaderParameter("BloomScreenBlendThresholdParameter"),
                    FShaderParameter("DistanceFadeParameter"),
                    FShaderResourceParameter("SourceTextureParameter"),
                    FShaderParameter("OcclusionValueLimit"),
                },
            };
        }

        BinInterpNode FDOFShaderParameters(string name)
        {
            return new BinInterpNode(bin.Position, $"{name}: FDOFShaderParameters")
            {
                Items =
                {
                    FShaderParameter("PackedParameters"),
                    FShaderParameter("MinMaxBlurClamp"),
                    FShaderResourceParameter("DOFTexture")
                },
            };
        }

        BinInterpNode FHBAOShaderParameters(string name)
        {
            return new BinInterpNode(bin.Position, $"{name}: {nameof(FHBAOShaderParameters)}")
            {
                Items =
                {
                    FShaderParameter("RadiusToScreen"),
                    FShaderParameter("NegInvR2"),
                    FShaderParameter("NDotVBias"),
                    FShaderParameter("AOMultiplier"),
                    FShaderParameter("PowExponent"),
                    FShaderParameter("ProjInfo"),
                    FShaderParameter("BlurSharpness"),
                    FShaderParameter("InvFullResolution"),
                    FShaderParameter("InvQuarterResolution"),
                    FShaderParameter("FullResOffset"),
                    FShaderParameter("QuarterResOffset"),
                },
            };
        }

        BinInterpNode FHeightFogVertexShaderParameters(string name)
        {
            return new BinInterpNode(bin.Position, $"{name}: FHeightFogVertexShaderParameters")
            {
                Items =
                {
                    FShaderParameter("FogDistanceScaleParameter"),
                    FShaderParameter("FogExtinctionDistanceParameter"),
                    FShaderParameter("FogMinHeightParameter"),
                    FShaderParameter("FogMaxHeightParameter"),
                    FShaderParameter("FogInScatteringParameter"),
                    FShaderParameter("FogStartDistanceParameter"),
                },
            };
        }

        BinInterpNode FForwardShadowingShaderParameters(string name)
        {
            return new BinInterpNode(bin.Position, $"{name}: FForwardShadowingShaderParameters")
            {
                Items =
                {
                    FShaderParameter("bReceiveDynamicShadows"),
                    FShaderParameter("ScreenToShadowMatrix"),
                    FShaderParameter("ShadowBufferAndTexelSize"),
                    FShaderParameter("ShadowOverrideFactor"),
                    FShaderResourceParameter("ShadowDepthTexture"),
                },
            };
        }

        void FBranchingPCFProjectionPixelShader()
        {
            node.Items.Add(FSceneTextureShaderParameters("SceneTextureParameters"));
            node.Items.Add(FShaderParameter("ScreenToShadowMatrixParameter"));
            node.Items.Add(FShaderParameter("InvRandomAngleTextureSize"));
            node.Items.Add(FShaderResourceParameter("ShadowDepthTextureParameter"));
            node.Items.Add(FShaderResourceParameter("RandomAngleTextureParameter"));
            node.Items.Add(FShaderParameter("RefiningSampleOffsetsParameter"));
            node.Items.Add(FShaderParameter("EdgeSampleOffsetsParameter"));
            node.Items.Add(FShaderParameter("ShadowBufferSizeParameter"));
            node.Items.Add(FShaderParameter("ShadowFadeFractionParameter"));
        }

        void FBranchingPCFModProjectionPixelShader()
        {
            FBranchingPCFProjectionPixelShader();
            node.Items.Add(FShaderParameter("ShadowModulateColorParam"));
            node.Items.Add(FShaderParameter("ScreenToWorldParam"));
            node.Items.Add(FShaderParameter("EmissiveAlphaMaskScale"));
            node.Items.Add(FShaderParameter("UseEmissiveMaskParameter"));
        }

        void FPointLightPolicy_ModShadowPixelParamsType()
        {
            node.Items.Add(FShaderParameter("LightPositionParam"));
            node.Items.Add(FShaderParameter("FalloffParameters"));
        }

        void FPointLightPolicy_PixelParametersType()
        {
            node.Items.Add(FShaderParameter("LightColorAndFalloffExponent"));
        }

        void FDirectionalLightPolicy_PixelParametersType()
        {
            node.Items.Add(FShaderParameter("LightColor"));
            node.Items.Add(FShaderParameter("bReceiveDynamicShadows"));
            node.Items.Add(FShaderParameter("bEnableDistanceShadowFading"));
            node.Items.Add(FShaderParameter("DistanceFadeParameters"));
        }

        void FSphericalHarmonicLightPolicy_PixelParametersType()
        {
            node.Items.Add(FShaderParameter("WorldIncidentLighting"));
        }

        void FModShadowVolumePixelShader_Maybe()
        {
            node.Items.Add(FSceneTextureShaderParameters("SceneTextureParameters"));
            node.Items.Add(FShaderParameter("ShadowModulateColor"));
            node.Items.Add(FShaderParameter("ScreenToWorld"));
        }

        void FSpotLightPolicy_ModShadowPixelParamsType()
        {
            node.Items.Add(FShaderParameter("LightPositionParam"));
            node.Items.Add(FShaderParameter("FalloffParameters"));
            node.Items.Add(FShaderParameter("SpotDirectionParam"));
            node.Items.Add(FShaderParameter("SpotAnglesParam"));
        }

        void FSpotLightPolicy_PixelParametersType()
        {
            node.Items.Add(FShaderParameter("SpotAngles"));
            node.Items.Add(FShaderParameter("SpotDirection"));
            node.Items.Add(FShaderParameter("LightColorAndFalloffExponent"));
        }

        void FLightMapTexturePolicy_PixelParametersType()
        {
            node.Items.Add(FShaderResourceParameter("LightMapTextures"));
            node.Items.Add(FShaderParameter("LightMapScale"));
        }

        void FLightMapTexturePolicy_VertexParametersType()
        {
            node.Items.Add(FShaderParameter("LightmapCoordinateScaleBias"));
        }

        void FVertexLightMapPolicy_VertexParametersType()
        {
            node.Items.Add(FShaderParameter("LightMapScale"));
        }

        void FDOFAndBloomBlendPixelShader()
        {
            node.Items.Add(FShaderParameter("PackedParameters?"));
            node.Items.Add(FShaderParameter("MinMaxBlurClampParameter?"));
            node.Items.Add(FShaderResourceParameter("unk"));
            node.Items.Add(FSceneTextureShaderParameters("SceneTextureParameters"));
            node.Items.Add(FShaderResourceParameter("BlurredImageParameter?"));
            node.Items.Add(FShaderResourceParameter("FilterColor2TextureParameter?"));
            node.Items.Add(FShaderResourceParameter("DoFBlurBufferParameter?"));
            node.Items.Add(FShaderResourceParameter("unk"));
            node.Items.Add(FShaderParameter("BloomTintAndScreenBlendThresholdParameter?"));
            node.Items.Add(FShaderParameter("unk"));
            node.Items.Add(FShaderParameter("unk"));
        }

        BinInterpNode FMotionBlurShaderParameters(string name)
        {
            var binInterpNode = new BinInterpNode(bin.Position, $"{name}: FColorRemapShaderParameters");
            binInterpNode.Items.Add(FShaderResourceParameter("LowResSceneBuffer"));
            binInterpNode.Items.Add(FShaderResourceParameter("VelocityBuffer"));
            binInterpNode.Items.Add(FShaderParameter("ScreenToWorldParameter"));
            binInterpNode.Items.Add(FShaderParameter("PrevViewProjParameter"));
            binInterpNode.Items.Add(FShaderParameter("StaticVelocityParameters"));
            binInterpNode.Items.Add(FShaderParameter("DynamicVelocityParameters"));
            binInterpNode.Items.Add(FShaderParameter("RenderTargetClampParameter"));
            binInterpNode.Items.Add(FShaderParameter("MotionBlurMaskScaleParameter"));
            binInterpNode.Items.Add(FShaderParameter("StepOffsetsOpaqueParameter"));
            binInterpNode.Items.Add(FShaderParameter("StepWeightsOpaqueParameter"));
            binInterpNode.Items.Add(FShaderParameter("StepOffsetsTranslucentParameter"));
            binInterpNode.Items.Add(FShaderParameter("StepWeightsTranslucentParameter"));
            return binInterpNode;
        }

        void FUberPostProcessBlendPixelShader()
        {
            FDOFAndBloomBlendPixelShader();
            node.Items.Add(FShaderParameter("unk"));
            node.Items.Add(FShaderParameter("unk"));
            node.Items.Add(FShaderParameter("unk"));
            node.Items.Add(FShaderParameter("unk"));
            node.Items.Add(FShaderParameter("unk"));
            node.Items.Add(FShaderParameter("unk"));
            node.Items.Add(FShaderResourceParameter("unk"));
            node.Items.Add(FShaderParameter("unk"));
            node.Items.Add(FShaderResourceParameter("unk"));
            node.Items.Add(FShaderParameter("unk"));
            node.Items.Add(FMotionBlurShaderParameters("MotionBlurParameters"));
        }

        void TModShadowProjectionPixelShader()
        {
            node.Items.Add(FSceneTextureShaderParameters("SceneTextureParameters?"));
            node.Items.Add(FShaderParameter("ScreenToShadowMatrixParameter?"));
            node.Items.Add(FShaderResourceParameter("ShadowDepthTextureParameter?"));
            node.Items.Add(FShaderResourceParameter("RandomAngleTextureParameter?"));
            node.Items.Add(FShaderParameter("RefiningSampleOffsetsParameter?"));
            node.Items.Add(FShaderParameter("EdgeSampleOffsetsParameter?"));
            node.Items.Add(FShaderParameter("ShadowBufferSizeParameter?"));
            node.Items.Add(FShaderParameter("ShadowFadeFractionParameter?"));
            node.Items.Add(FShaderParameter("ShadowModulateColorParam?"));
            node.Items.Add(FShaderParameter("ScreenToWorldParam?"));
            node.Items.Add(FShaderParameter("EmissiveAlphaMaskScale?"));
        }

        void FSHLightLightMapPolicy_PixelParametersType()
        {
            node.Items.Add(FShaderParameter("LightColorAndFalloffExponent"));
            node.Items.Add(FShaderParameter("bReceiveDynamicShadows"));
            node.Items.Add(FShaderParameter("WorldIncidentLighting"));
        }

        void TDOFAndBloomGatherPixelShader()
        {
            node.Items.Add(FDOFShaderParameters("DOFParameters"));
            node.Items.Add(FSceneTextureShaderParameters("SceneTextureParameters"));
            node.Items.Add(FShaderParameter("BloomScaleAndThreshold"));
            node.Items.Add(FShaderParameter("SceneMultiplier"));
            node.Items.Add(FShaderResourceParameter("SmallSceneColorTexture"));
        }

        void FSimpleElementGammaPixelShader()
        {
            FSimpleElementPixelShader();
            node.Items.Add(FShaderParameter("Gamma"));
        }

        void FSimpleElementPixelShader()
        {
            node.Items.Add(FShaderResourceParameter("_Texture"));
            node.Items.Add(FShaderParameter("TextureComponentReplicate"));
            node.Items.Add(FShaderParameter("TextureComponentReplicateAlpha"));
        }

        void FDirectionalLightPolicy_VertexParametersType()
        {
            node.Items.Add(FShaderParameter("LightDirection"));
        }

        void FPointLightPolicy_VertexParametersType()
        {
            node.Items.Add(FShaderParameter("LightPositionAndInvRadius"));
        }

        void FSFXPointLightPolicy_PixelParametersType()
        {
            node.Items.Add(FShaderResourceParameter("LightSpaceShadowMap"));
            node.Items.Add(FShaderParameter("LightColorAndFalloffExponent"));
            node.Items.Add(FShaderParameter("ShadowFilter"));
            node.Items.Add(FShaderParameter("ShadowTextureRegion"));
            node.Items.Add(FShaderParameter("MaxVarianceShadowAttenuation"));
        }

        void FSFXPointLightPolicy_VertexParametersType()
        {
            node.Items.Add(FShaderParameter("LightPositionAndInvRadius"));
            node.Items.Add(FShaderParameter("ShadowViewProjection"));
        }

        void FDirectionalLightLightMapPolicy_VertexParametersType()
        {
            node.Items.Add(FShaderParameter("LightDirectionAndbDirectional"));
        }

        void FDirectionalLightLightMapPolicy_PixelParametersType()
        {
            node.Items.Add(FShaderParameter("LightColorAndFalloffExponent"));
            node.Items.Add(FShaderParameter("bReceiveDynamicShadows"));
        }

        void TBasePassPixelShader()
        {
            node.Items.Add(FMaterialPixelShaderParameters("MaterialParameters"));
            node.Items.Add(FShaderParameter("AmbientColorAndSkyFactor"));
            node.Items.Add(FShaderParameter("UpperSkyColor"));
            node.Items.Add(FShaderParameter("LowerSkyColor"));
            node.Items.Add(FShaderParameter("MotionBlurMask"));
            node.Items.Add(FShaderParameter("CharacterMask"));
            node.Items.Add(FShaderParameter("TranslucencyDepth"));
        }

        void TBasePassVertexShader()
        {
            node.Items.Add(FVertexFactoryParameterRef());
            node.Items.Add(FHeightFogVertexShaderParameters("HeightFogParameters"));
            node.Items.Add(FMaterialVertexShaderParameters("MaterialParameters"));
        }

        void TLightPixelShader()
        {
            node.Items.Add(FMaterialPixelShaderParameters("MaterialParameters"));
            node.Items.Add(FShaderResourceParameter("LightAttenuationTexture"));
            node.Items.Add(FShaderParameter("bReceiveDynamicShadows"));
        }
    }

    //For Consoles
    private List<ITreeItem> StartShaderCachePayloadScanStream(byte[] data, ref int binarystart)
    {
        var subnodes = new List<ITreeItem>();
        try
        {
            var export = CurrentLoadedExport; //Prevents losing the reference
            int dataOffset = export.DataOffset;
            var bin = new EndianReader(new MemoryStream(data)) { Endian = CurrentLoadedExport.FileRef.Endian };
            bin.JumpTo(binarystart);

            var platformByte = bin.ReadByte();
            if (export.Game.IsLEGame())
            {
                var platform = (EShaderPlatformOT)bin.ReadByte();
                subnodes.Add(new BinInterpNode(bin.Position, $"Platform: {platform}") { Length = 1 });
            }
            else
            {
                var platform = (EShaderPlatformLE)bin.ReadByte();
                subnodes.Add(new BinInterpNode(bin.Position, $"Platform: {platform}") { Length = 1 });
            }

            //if (platform != EShaderPlatform.XBOXDirect3D){
            int mapCount = (Pcc.Game == MEGame.ME3 || Pcc.Game == MEGame.UDK) ? 2 : 1;
            if (platformByte == (byte)EShaderPlatformOT.XBOXDirect3D && !export.Game.IsLEGame()) mapCount = 1;
            var nameMappings = new[] { "CompressedCacheMap", "ShaderTypeCRCMap" };
            while (mapCount > 0)
            {
                mapCount--;
                int vertexMapCount = bin.ReadInt32();
                var mappingNode = new BinInterpNode(bin.Position - 4, $"{nameMappings[mapCount]}, {vertexMapCount} items");
                subnodes.Add(mappingNode);

                for (int i = 0; i < vertexMapCount; i++)
                {
                    NameReference shaderName = bin.ReadNameReference(Pcc);
                    int shaderCRC = bin.ReadInt32();
                    mappingNode.Items.Add(new BinInterpNode(bin.Position - 12, $"CRC:{shaderCRC:X8} {shaderName.Instanced}") { Length = 12 });
                }
            }

            //if (export.FileRef.Platform != MEPackage.GamePlatform.Xenon && export.FileRef.Game == MEGame.ME3)
            //{
            //    subnodes.Add(MakeInt32Node(bin, "PS3/WiiU Count of something??"));
            //}

            //subnodes.Add(MakeInt32Node(bin, "???"));
            //subnodes.Add(MakeInt32Node(bin, "Shader File Count?"));

            int embeddedShaderFileCount = bin.ReadInt32();
            var embeddedShaderCount = new BinInterpNode(bin.Position - 4, $"Embedded Shader File Count: {embeddedShaderFileCount}");
            subnodes.Add(embeddedShaderCount);
            for (int i = 0; i < embeddedShaderFileCount; i++)
            {
                NameReference shaderName = bin.ReadNameReference(Pcc);
                var shaderNode = new BinInterpNode(bin.Position - 8, $"Shader {i} {shaderName.Instanced}");
                try
                {
                    shaderNode.Items.Add(new BinInterpNode(bin.Position - 8, $"Shader Type: {shaderName.Instanced}")
                        { Length = 8 });
                    shaderNode.Items.Add(new BinInterpNode(bin.Position, $"Shader GUID {bin.ReadGuid()}")
                        { Length = 16 });
                    if (Pcc.Game == MEGame.UDK)
                    {
                        shaderNode.Items.Add(MakeGuidNode(bin, "2nd Guid?"));
                        shaderNode.Items.Add(MakeUInt32Node(bin, "unk?"));
                    }

                    int shaderEndOffset = bin.ReadInt32();
                    shaderNode.Items.Add(
                        new BinInterpNode(bin.Position - 4, $"Shader End Offset: {shaderEndOffset}") { Length = 4 });

                    if (export.Game.IsLEGame())
                    {
                        shaderNode.Items.Add(
                            new BinInterpNode(bin.Position, $"Platform: {(EShaderPlatformLE)bin.ReadByte()}")
                                { Length = 1 });
                    }
                    else
                    {
                        shaderNode.Items.Add(
                            new BinInterpNode(bin.Position, $"Platform: {(EShaderPlatformOT)bin.ReadByte()}")
                                { Length = 1 });
                    }

                    shaderNode.Items.Add(new BinInterpNode(bin.Position,
                            $"Frequency: {(EShaderFrequency)bin.ReadByte()}")
                        { Length = 1 });

                    int shaderSize = bin.ReadInt32();
                    shaderNode.Items.Add(new BinInterpNode(bin.Position - 4, $"Shader File Size: {shaderSize}")
                        { Length = 4 });

                    shaderNode.Items.Add(new BinInterpNode(bin.Position, "Shader File") { Length = shaderSize });
                    bin.Skip(shaderSize);

                    shaderNode.Items.Add(MakeInt32Node(bin, "ParameterMap CRC"));

                    shaderNode.Items.Add(new BinInterpNode(bin.Position, $"Shader End GUID: {bin.ReadGuid()}")
                        { Length = 16 });

                    shaderNode.Items.Add(
                        new BinInterpNode(bin.Position, $"Shader Type: {bin.ReadNameReference(Pcc)}") { Length = 8 });

                    shaderNode.Items.Add(MakeInt32Node(bin, "Number of Instructions"));

                    shaderNode.Items.Add(
                        new BinInterpNode(bin.Position,
                                $"Unknown shader bytes ({shaderEndOffset - (dataOffset + bin.Position)} bytes)")
                            { Length = (int)(shaderEndOffset - (dataOffset + bin.Position)) });

                    embeddedShaderCount.Items.Add(shaderNode);

                    bin.JumpTo(shaderEndOffset - dataOffset);
                }
                catch (Exception)
                {
                    embeddedShaderCount.Items.Add(shaderNode);
                    break;
                }
            }

            /*
                int mapCount = Pcc.Game >= MEGame.ME3 ? 2 : 1;
                for (; mapCount > 0; mapCount--)
                {
                    int vertexMapCount = bin.ReadInt32();
                    var mappingNode = new BinInterpNode(bin.Position - 4, $"Name Mapping {mapCount}, {vertexMapCount} items");
                    subnodes.Add(mappingNode);

                    for (int i = 0; i < vertexMapCount; i++)
                    {
                        NameReference shaderName = bin.ReadNameReference(Pcc);
                        int shaderCRC = bin.ReadInt32();
                        mappingNode.Items.Add(new BinInterpNode(bin.Position - 12, $"CRC:{shaderCRC:X8} {shaderName.Instanced}") { Length = 12 });
                    }
                }

                if (Pcc.Game == MEGame.ME1)
                {
                    ReadVertexFactoryMap();
                }

                int embeddedShaderFileCount = bin.ReadInt32();
                var embeddedShaderCount = new BinInterpNode(bin.Position - 4, $"Embedded Shader File Count: {embeddedShaderFileCount}");
                subnodes.Add(embeddedShaderCount);
                for (int i = 0; i < embeddedShaderFileCount; i++)
                {
                    NameReference shaderName = bin.ReadNameReference(Pcc);
                    var shaderNode = new BinInterpNode(bin.Position - 8, $"Shader {i} {shaderName.Instanced}");

                    shaderNode.Items.Add(new BinInterpNode(bin.Position - 8, $"Shader Type: {shaderName.Instanced}") { Length = 8 });
                    shaderNode.Items.Add(new BinInterpNode(bin.Position, $"Shader GUID {bin.ReadGuid()}") { Length = 16 });
                    if (Pcc.Game == MEGame.UDK)
                    {
                        shaderNode.Items.Add(MakeGuidNode(bin, "2nd Guid?"));
                        shaderNode.Items.Add(MakeUInt32Node(bin, "unk?"));
                    }
                    int shaderEndOffset = bin.ReadInt32();
                    shaderNode.Items.Add(new BinInterpNode(bin.Position - 4, $"Shader End Offset: {shaderEndOffset}") { Length = 4 });


                    shaderNode.Items.Add(new BinInterpNode(bin.Position, $"Platform: {(EShaderPlatform)bin.ReadByte()}") { Length = 1 });
                    shaderNode.Items.Add(new BinInterpNode(bin.Position, $"Frequency: {(EShaderFrequency)bin.ReadByte()}") { Length = 1 });

                    int shaderSize = bin.ReadInt32();
                    shaderNode.Items.Add(new BinInterpNode(bin.Position - 4, $"Shader File Size: {shaderSize}") { Length = 4 });

                    shaderNode.Items.Add(new BinInterpNode(bin.Position, "Shader File") { Length = shaderSize });
                    bin.Skip(shaderSize);

                    shaderNode.Items.Add(MakeInt32Node(bin, "ParameterMap CRC"));

                    shaderNode.Items.Add(new BinInterpNode(bin.Position, $"Shader End GUID: {bin.ReadGuid()}") { Length = 16 });

                    shaderNode.Items.Add(new BinInterpNode(bin.Position, $"Shader Type: {bin.ReadNameReference(Pcc)}") { Length = 8 });

                    shaderNode.Items.Add(MakeInt32Node(bin, "Number of Instructions"));

                    embeddedShaderCount.Items.Add(shaderNode);

                    bin.JumpTo(shaderEndOffset - dataOffset);
                }

                void ReadVertexFactoryMap()
                {
                    int vertexFactoryMapCount = bin.ReadInt32();
                    var factoryMapNode = new BinInterpNode(bin.Position - 4, $"Vertex Factory Name Mapping, {vertexFactoryMapCount} items");
                    subnodes.Add(factoryMapNode);

                    for (int i = 0; i < vertexFactoryMapCount; i++)
                    {
                        NameReference shaderName = bin.ReadNameReference(Pcc);
                        int shaderCRC = bin.ReadInt32();
                        factoryMapNode.Items.Add(new BinInterpNode(bin.Position - 12, $"{shaderCRC:X8} {shaderName.Instanced}") { Length = 12 });
                    }
                }
                if (Pcc.Game == MEGame.ME2 || Pcc.Game == MEGame.ME3)
                {
                    ReadVertexFactoryMap();
                }

                int materialShaderMapcount = bin.ReadInt32();
                var materialShaderMaps = new BinInterpNode(bin.Position - 4, $"Material Shader Maps, {materialShaderMapcount} items");
                subnodes.Add(materialShaderMaps);
                for (int i = 0; i < materialShaderMapcount; i++)
                {
                    var nodes = new List<ITreeItem>();
                    materialShaderMaps.Items.Add(new BinInterpNode(bin.Position, $"Material Shader Map {i}") { Items = nodes });
                    nodes.Add(ReadFStaticParameterSet(bin));

                    if (Pcc.Game >= MEGame.ME3)
                    {
                        nodes.Add(new BinInterpNode(bin.Position, $"Unreal Version {bin.ReadInt32()}") { Length = 4 });
                        nodes.Add(new BinInterpNode(bin.Position, $"Licensee Version {bin.ReadInt32()}") { Length = 4 });
                    }

                    int shaderMapEndOffset = bin.ReadInt32();
                    nodes.Add(new BinInterpNode(bin.Position - 4, $"Material Shader Map end offset {shaderMapEndOffset}") { Length = 4 });

                    int unkCount = bin.ReadInt32();
                    var unkNodes = new List<ITreeItem>();
                    nodes.Add(new BinInterpNode(bin.Position - 4, $"Shaders {unkCount}") { Length = 4, Items = unkNodes });
                    for (int j = 0; j < unkCount; j++)
                    {
                        unkNodes.Add(new BinInterpNode(bin.Position, $"Shader Type: {bin.ReadNameReference(Pcc).Instanced}") { Length = 8 });
                        unkNodes.Add(new BinInterpNode(bin.Position, $"GUID: {bin.ReadGuid()}") { Length = 16 });
                        unkNodes.Add(new BinInterpNode(bin.Position, $"Shader Type: {bin.ReadNameReference(Pcc).Instanced}") { Length = 8 });
                    }

                    int meshShaderMapsCount = bin.ReadInt32();
                    var meshShaderMaps = new BinInterpNode(bin.Position - 4, $"Mesh Shader Maps, {meshShaderMapsCount} items") { Length = 4 };
                    nodes.Add(meshShaderMaps);
                    for (int j = 0; j < meshShaderMapsCount; j++)
                    {
                        var nodes2 = new List<ITreeItem>();
                        meshShaderMaps.Items.Add(new BinInterpNode(bin.Position, $"Mesh Shader Map {j}") { Items = nodes2 });

                        int shaderCount = bin.ReadInt32();
                        var shaders = new BinInterpNode(bin.Position - 4, $"Shaders, {shaderCount} items") { Length = 4 };
                        nodes2.Add(shaders);
                        for (int k = 0; k < shaderCount; k++)
                        {
                            var nodes3 = new List<ITreeItem>();
                            shaders.Items.Add(new BinInterpNode(bin.Position, $"Shader {k}") { Items = nodes3 });

                            nodes3.Add(new BinInterpNode(bin.Position, $"Shader Type: {bin.ReadNameReference(Pcc)}") { Length = 8 });
                            nodes3.Add(new BinInterpNode(bin.Position, $"GUID: {bin.ReadGuid()}") { Length = 16 });
                            nodes3.Add(new BinInterpNode(bin.Position, $"Shader Type: {bin.ReadNameReference(Pcc)}") { Length = 8 });
                        }
                        nodes2.Add(new BinInterpNode(bin.Position, $"Vertex Factory Type: {bin.ReadNameReference(Pcc)}") { Length = 8 });
                        if (Pcc.Game == MEGame.ME1)
                        {
                            nodes2.Add(MakeUInt32Node(bin, "Unk"));
                        }
                    }

                    nodes.Add(new BinInterpNode(bin.Position, $"MaterialId: {bin.ReadGuid()}") { Length = 16 });

                    nodes.Add(MakeStringNode(bin, "Friendly Name"));

                    nodes.Add(ReadFStaticParameterSet(bin));

                    if (Pcc.Game >= MEGame.ME3)
                    {
                        string[] uniformExpressionArrays =
                        {
                            "UniformPixelVectorExpressions",
                            "UniformPixelScalarExpressions",
                            "Uniform2DTextureExpressions",
                            "UniformCubeTextureExpressions",
                            "UniformVertexVectorExpressions",
                            "UniformVertexScalarExpressions"
                        };

                        foreach (string uniformExpressionArrayName in uniformExpressionArrays)
                        {
                            int expressionCount = bin.ReadInt32();
                            nodes.Add(new BinInterpNode(bin.Position - 4, $"{uniformExpressionArrayName}, {expressionCount} expressions")
                            {
                                Items = ReadList(expressionCount, x => ReadMaterialUniformExpression(bin))
                            });
                        }
                        nodes.Add(new BinInterpNode(bin.Position, $"Platform: {(EShaderPlatform)bin.ReadInt32()}") { Length = 4 });
                    }

                    bin.JumpTo(shaderMapEndOffset - dataOffset);
                }

                int numShaderCachePayloads = bin.ReadInt32();
                var shaderCachePayloads = new BinInterpNode(bin.Position - 4, $"Shader Cache Payloads, {numShaderCachePayloads} items");
                subnodes.Add(shaderCachePayloads);
                for (int i = 0; i < numShaderCachePayloads; i++)
                {
                    shaderCachePayloads.Items.Add(MakeEntryNode(bin, $"Payload {i}"));
                } */
        }
        catch (Exception ex)
        {
            subnodes.Add(new BinInterpNode { Header = $"Error reading binary data: {ex}" });
        }

        return subnodes;
    }
}