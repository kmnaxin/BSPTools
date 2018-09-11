﻿using BSPEngine;
using BSPGenerationTools;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace GeneratorSampleStm32
{

    class Program
    {
        static public List<string> ToAbsolutePath(string dir, string topLevelDir, List<string> lstDir)
        {
            List<string> srcAbc = new List<string>();

            foreach (var sf in lstDir)
            {
                string fn = sf;
                fn = fn.Replace(@"/RVDS/", @"/GCC/");
                fn = fn.Replace(@"\RVDS\", @"\GCC\");

                if (!Path.IsPathRooted(fn))
                    fn = Path.GetFullPath(Path.Combine(dir, fn));

                if (!File.Exists(fn) && !Directory.Exists(fn))
                {
                    if (Path.GetFileName(fn).ToLower() == "readme.txt" || Path.GetFileName(fn).ToLower() == "ipv4")
                        continue;

                    if (fn.EndsWith("\\component", StringComparison.InvariantCultureIgnoreCase) && Directory.Exists(fn + "s"))
                        fn += "s";
                    else
                    {
                        string fn2 = Path.Combine(topLevelDir, sf);
                        if (Directory.Exists(fn2))
                            fn = fn2;
                        else
                            Console.WriteLine("Missing file/directory: " + fn);
                    }
                }

                srcAbc.Add(fn);
            }
            return srcAbc;
        }

        static public List<VendorSample> GetInfoProjectFromMDK(string pDirPrj, string topLevelDir, List<string> extraIncludeDirs)
        {
            List<VendorSample> aLstVSampleOut = new List<VendorSample>();
            int aCntTarget = 0;
            string aFilePrj = pDirPrj + "\\Project.uvprojx";
            string aNamePrj = pDirPrj.Replace("\\MDK-ARM", "");
            aNamePrj = aNamePrj.Substring(aNamePrj.LastIndexOf("\\") + 1);
            List<string> sourceFiles = new List<string>();
            List<string> includeDirs = new List<string>();
            bool flGetProperty = false;
            string aTarget = "";
            VendorSample sample = new VendorSample();

            foreach (var ln in File.ReadAllLines(aFilePrj))
            {
                if (ln.Contains("<Target>"))
                {
                    if (aCntTarget == 0)
                        sample = new VendorSample();
                    aCntTarget++;
                }
                if (ln.Contains("</Target>"))
                    if (aCntTarget == 0)
                        throw new Exception("wrong tag Targets");
                    else
                        aCntTarget--;

                if (ln.Contains("<Cads>"))
                    flGetProperty = true;
                else if (ln.Contains("</Cads>"))
                    flGetProperty = false;

                Match m = Regex.Match(ln, "[ \t]*<Device>(.*)</Device>[ \t]*");
                if (m.Success)
                {
                    sample.DeviceID = m.Groups[1].Value;
                    if (sample.DeviceID.EndsWith("x"))
                        sample.DeviceID = sample.DeviceID.Remove(sample.DeviceID.Length - 2, 2);
                }
                m = Regex.Match(ln, "[ \t]*<TargetName>(.*)</TargetName>[ \t]*");
                if (m.Success)
                    aTarget = m.Groups[1].Value;

                MatchCollection m1 = Regex.Matches(ln, @"[ ]*<FilePath>([\w\-:\\./]*)</FilePath>[ ]*");
                foreach (Match mc in m1)
                {
                    string filePath = mc.Groups[1].Value;
                    if (filePath.StartsWith(@"./") || filePath.StartsWith(@".\"))
                        filePath = pDirPrj + filePath.Substring(1);
                    if (filePath.EndsWith(".s", StringComparison.InvariantCultureIgnoreCase))
                        continue;

                    if (filePath.EndsWith(".lib", StringComparison.InvariantCultureIgnoreCase))
                    {
                        filePath = filePath.Replace("_Keil.lib", "_GCC.a");
                        if (!File.Exists(Path.Combine(pDirPrj, filePath)))
                            continue;
                    }
                    if (filePath.EndsWith(".a", StringComparison.InvariantCultureIgnoreCase) && filePath.IndexOf("_IAR", StringComparison.InvariantCultureIgnoreCase) != -1)
                    {
                        filePath = filePath.Replace("_IAR_ARGB.a", "_GCC.a");
                        if (!File.Exists(Path.Combine(pDirPrj, filePath)))
                            continue;
                    }

                    if (!sourceFiles.Contains(filePath))
                        sourceFiles.Add(filePath);
                }

                if (flGetProperty)
                {
                    m = Regex.Match(ln, "[ \t]*<IncludePath>(.*)</IncludePath>[ \t]*");
                    if (m.Success && m.Groups[1].Value != "")
                        sample.IncludeDirectories = m.Groups[1].Value.Split(';').Select(d=>d.TrimEnd('/', '\\')).ToArray();

                    m = Regex.Match(ln, "[ \t]*<Define>(.*)</Define>[ \t]*");
                    if (m.Success && m.Groups[1].Value != "")
                        sample.PreprocessorMacros = m.Groups[1].Value.Split(',');
                }


                if (ln.Contains("</Target>") && aCntTarget == 0)
                {
                    sample.Path = Path.GetDirectoryName(pDirPrj);
                    sample.UserFriendlyName = aNamePrj;
                    sample.BoardName = aTarget;
                    sample.SourceFiles = ToAbsolutePath(pDirPrj, topLevelDir, sourceFiles).ToArray();

                    foreach (var fl in sample.IncludeDirectories)
                        includeDirs.Add(fl);
                    includeDirs.AddRange(extraIncludeDirs);
                    sample.IncludeDirectories = ToAbsolutePath(pDirPrj, topLevelDir, includeDirs).ToArray();

                    string readmeFile = Path.Combine(pDirPrj, @"..\readme.txt");
                    if (File.Exists(readmeFile))
                    {
                        string readmeContents = File.ReadAllText(readmeFile);
                        Regex rgTitle = new Regex(@"@page[ \t]+[^ \t]+[ \t]+([^ \t][^@]*)\r\n *\r\n", RegexOptions.Singleline);
                        m = rgTitle.Match(readmeContents);
                        if (m.Success)
                        {
                            sample.Description = m.Groups[1].Value;
                        }
                    }

                    aLstVSampleOut.Add(sample);
                }
            }
            return aLstVSampleOut;
        }

        static string ExtractFirstSubdir(string dir) => dir.Split('\\')[1];

        class STM32SampleRelocator : VendorSampleRelocator
        {
            public STM32SampleRelocator()
            {
                /*
                    Known problems with trying to map frameworks:
                      HAL:
                        * Much longer build times
                        * LL-only samples don't provide cfg files for HAL
                        * HAL-only samples don't provide stm32_assert.h needed by LL
                      lwIP:
                        * Different SDKs have slightly different file layouts
                        * Some samples don't provide sys_xxx() functions
                */

                AutoDetectedFrameworks = new AutoDetectedFramework[]
                {/*
                    new AutoDetectedFramework {FrameworkID = "com.sysprogs.arm.stm32.hal",
                        FileRegex = new Regex(@"\$\$SYS:VSAMPLE_DIR\$\$/[^/\\]+/Drivers/[^/\\]+_HAL_Driver", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                        DisableTriggerRegex = new Regex(@"_ll_[^/\\]+\.c", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                        Configuration = new Dictionary<string, string>() }
                        */

                    /*new AutoDetectedFramework {FrameworkID = "com.sysprogs.arm.stm32.LwIP",
                        FileRegex = new Regex(@"\$\$SYS:VSAMPLE_DIR\$\$/[^/\\]+/Middlewares/Third_Party/LwIP", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                        DisableTriggerRegex = new Regex(@"^$", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                        Configuration = new Dictionary<string, string>() }*/
                };

                AutoPathMappings = new PathMapping[]
                {
                    new PathMapping(@"\$\$SYS:VSAMPLE_DIR\$\$/STM32Cube_FW_([^_]+)_[^/\\]+/Drivers/STM32[^/\\]+xx_HAL_Driver/(.*)", "$$SYS:BSP_ROOT$$/STM32{1}xxxx/STM32{1}xx_HAL_Driver/{2}"),
                    new PathMapping(@"\$\$SYS:VSAMPLE_DIR\$\$/STM32Cube_FW_([^_]+)_[^/\\]+/Drivers/CMSIS/(.*)", "$$SYS:BSP_ROOT$$/STM32{1}xxxx/CMSIS_HAL/{2}"),

                    new PathMapping(@"\$\$SYS:VSAMPLE_DIR\$\$/STM32Cube_FW_([^_]+)_[^/\\]+/Middlewares/ST/STM32_USB_(Host|Device)_Library/(.*)", "$$SYS:BSP_ROOT$$/STM32_USB_{2}_Library/{3}"),
                    new PathMapping(@"\$\$SYS:VSAMPLE_DIR\$\$/STM32Cube_FW_([^_]+)_[^/\\]+/Middlewares/Third_Party/(FreeRTOS)/(.*)", "$$SYS:BSP_ROOT$$/{2}/{3}"),
                };
            }

            protected override void FilterPreprocessorMacros(ref string[] macros)
            {
                base.FilterPreprocessorMacros(ref macros);
                macros = macros.Where(m => !m.StartsWith("STM32") || m.Contains("_")).Concat(new string[] { "$$com.sysprogs.stm32.hal_device_family$$" }).ToArray();
            }

            protected override string BuildVirtualSamplePath(string originalPath)
            {
                return string.Join("\\", originalPath.Split('/').Skip(2).Reverse().Skip(1).Reverse());
            }
        }
        class STM32PathMapper : VendorSampleRelocator.PathMapper
        {
            public STM32PathMapper(ConstructedVendorSampleDirectory dir) : base(dir)
            {
            }

            const string Prefix1 = @"C:/QuickStep/STM32Cube_FW_H7_clean/Firmware";

            Regex rgFWFolder = new Regex(@"^(\$\$SYS:VSAMPLE_DIR\$\$)/STM32Cube_FW_([^_]+)_V[^/]+/(.*)$", RegexOptions.IgnoreCase);

            public override string MapPath(string path)
            {
                if (path?.StartsWith(Prefix1) == true)
                {
                    path = $@"{_SampleDir.SourceDirectory}\STM32Cube_FW_H7_V1.3.0{path.Substring(Prefix1.Length)}";
                    if (!File.Exists(path) && !Directory.Exists(path))
                        throw new Exception("Missing " + path);
                }

                string result = base.MapPath(path);
                if (result?.StartsWith("$$SYS:VSAMPLE_DIR$$/") == true)
                {
                    var m = rgFWFolder.Match(result);
                    if (!m.Success)
                        throw new Exception("Unexpected path format: " + result);

                    result = $"{m.Groups[1]}/{m.Groups[2]}/{m.Groups[3]}";
                }

                return result;
            }
        }


        static void Main(string[] args)
        {
            if (args.Length < 2)
                throw new Exception("Usage: stm32.exe <SW package directory> <temporary directory>");
            string SDKdir = args[0];
            string outputDir = @"..\..\Output";
            const string bspDir = @"..\..\..\..\generators\stm32\output";
            string tempDir = args[1];

            string sampleListFile = Path.Combine(outputDir, "samples.xml");

            var sampleDir = BuildOrLoadSampleDirectory(SDKdir, outputDir, sampleListFile);

            if (sampleDir.Samples.FirstOrDefault(s => s.AllDependencies != null) == null)
            {
                //Perform Pass 1 testing - test the raw VendorSamples in-place
                StandaloneBSPValidator.Program.TestVendorSamples(sampleDir, bspDir, tempDir);
                XmlTools.SaveObject(sampleDir, sampleListFile);
            }

            //Insert the samples into the generated BSP
            var relocator = new STM32SampleRelocator();
            relocator.InsertVendorSamplesIntoBSP(sampleDir, bspDir, new STM32PathMapper(sampleDir));

            var bsp = XmlTools.LoadObject<BoardSupportPackage>(Path.Combine(bspDir, "bsp.xml"));
            bsp.VendorSampleDirectoryPath = "VendorSamples";
            bsp.VendorSampleCatalogName = "STM32 CubeMX Samples";
            XmlTools.SaveObject(bsp, Path.Combine(bspDir, "bsp.xml"));

            string archiveName = string.Format("{0}-{1}.vgdbxbsp", bsp.PackageID.Split('.').Last(), bsp.PackageVersion);
            string statFile = Path.ChangeExtension(archiveName, ".xml");
            TarPacker.PackDirectoryToTGZ(bspDir, Path.Combine(bspDir, archiveName), fn => Path.GetExtension(fn).ToLower() != ".vgdbxbsp" && Path.GetFileName(fn) != statFile);

            // Finally verify that everything builds
            var expandedSamples = XmlTools.LoadObject<VendorSampleDirectory>(Path.Combine(bspDir, "VendorSamples", "VendorSamples.xml"));
            expandedSamples.Path = Path.GetFullPath(Path.Combine(bspDir, "VendorSamples"));
            var result = StandaloneBSPValidator.Program.TestVendorSamples(expandedSamples, bspDir, tempDir, 1);

            if (result.Failed != 0)
                throw new Exception("Some of the vendor samples have failed the internal test. Fix this before releasing the BSP.");
        }

        private static ConstructedVendorSampleDirectory BuildOrLoadSampleDirectory(string SDKdir, string outputDir, string sampleListFile)
        {
            ConstructedVendorSampleDirectory sampleDir;
            if (File.Exists(sampleListFile) || File.Exists(sampleListFile + ".gz"))
            {
                sampleDir = XmlTools.LoadObject<ConstructedVendorSampleDirectory>(sampleListFile);
                if (sampleDir.SourceDirectory == SDKdir)
                    return sampleDir;
            }

            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, true);
            Directory.CreateDirectory(outputDir);

            var samples = ParseVendorSamples(SDKdir);
            sampleDir = new ConstructedVendorSampleDirectory
            {
                SourceDirectory = SDKdir,
                Samples = samples.ToArray(),
            };

            XmlTools.SaveObject(sampleDir, sampleListFile);
            return sampleDir;
        }

        static List<VendorSample> ParseVendorSamples(string SDKdir)
        {
            string stm32RulesDir = @"..\..\..\..\generators\stm32\rules\families";

            string[] familyDirs = Directory.GetFiles(stm32RulesDir, "stm32*.xml").Select(f => ExtractFirstSubdir(XmlTools.LoadObject<FamilyDefinition>(f).PrimaryHeaderDir)).ToArray();
            List<VendorSample> allSamples = new List<VendorSample>();

            foreach (var fam in familyDirs)
            {
                List<string> addInc = new List<string>();
                addInc.Add($@"{SDKdir}\{fam}\Drivers\CMSIS\Include");
                string topLevelDir = $@"{SDKdir}\{fam}";

                int aCountSampl = 0;
                Console.Write($"Discovering samples for {fam}...");

                foreach (var dir in Directory.GetDirectories(Path.Combine(SDKdir, fam), "Mdk-arm", SearchOption.AllDirectories))
                {
                    if (!dir.Contains("Projects") || !(dir.Contains("Examples") || dir.Contains("Applications")))
                        continue;

                    var aSamples = GetInfoProjectFromMDK(dir, topLevelDir, addInc);
                    var scriptDir = Path.Combine(dir, "..", "SW4STM32");
                    if (Directory.Exists(scriptDir))
                    {
                        string[] linkerScripts = Directory.GetFiles(scriptDir, "*.ld", SearchOption.AllDirectories);
                        if (linkerScripts.Length == 1)
                        {
                            foreach (var sample in aSamples)
                                sample.LinkerScript = Path.GetFullPath(linkerScripts[0]);
                        }
                        else
                        {
                            //Some sample projects define multiple linker scripts (e.g. STM32F072RBTx_FLASH.ld vs. STM32F072VBTx_FLASH.ld).
                            //In this case we don't pick the sample-specific linker script and instead go with the regular BSP script for the selected MCU.s
                        }
                    }

                    aCountSampl += aSamples.Count;
                    allSamples.AddRange(aSamples);
                }
                Console.WriteLine($" {aCountSampl} samples found");
            }
            return allSamples;
        }
    }
}
