﻿using System;
using System.Collections.Generic;
using System.Linq;
using LiquidBackend.Domain;
using LiquidBackend.Util;
using NUnit.Framework;

namespace LiquidTest
{
    public class InformedWorkflowTests
    {
        [Test]
        public void TestSingleConfidentTarget()
        {
            // Testing PE(18:3/20:5) +H
            const string commonName = "PE(18:3/20:5)";
            const string empiricalFormula = "C43H71NO8P";
            const LipidClass lipidClass = LipidClass.PE;
            const FragmentationMode fragmentationMode = FragmentationMode.Positive;
            var acylChainList = new List<AcylChain> { new AcylChain("18:3"), new AcylChain("20:5") };

            var lipidTarget = LipidUtil.CreateLipidTarget(commonName, empiricalFormula, lipidClass, fragmentationMode, acylChainList);

            var rawFileLocation = @"../../../testFiles/Daphnia_gut_TLE_POS_Gimli_21Jan14_13-07-01.raw";
            var informedWorkflow = new InformedWorkflow(rawFileLocation);
            var resultList = informedWorkflow.RunInformedWorkflow(lipidTarget, 30, 500);

            foreach (var result in resultList.OrderByDescending(x => x.GetNumMatchingMsMsPeaks()))
            {
                Console.WriteLine(result.Xic.GetApexScanNum() + " - " + result.HcdSpectrum.ScanNum + " - " + result.CidSpectrum.ScanNum + " - " + result.GetNumMatchingMsMsPeaks());
            }
        }

        [Test]
        public void TestSingleTarget()
        {
            // Testing PC(16:0/18:1) +H
            const string commonName = "PC(16:0/18:1)";
            const string empiricalFormula = "C42H83NO8P";
            const LipidClass lipidClass = LipidClass.PC;
            const FragmentationMode fragmentationMode = FragmentationMode.Positive;
            var acylChainList = new List<AcylChain> { new AcylChain("16:0"), new AcylChain("18:1") };

            var lipidTarget = LipidUtil.CreateLipidTarget(commonName, empiricalFormula, lipidClass, fragmentationMode, acylChainList);

            var rawFileLocation = @"../../../testFiles/XGA121_lipid_Skin_1.raw";
            var informedWorkflow = new InformedWorkflow(rawFileLocation);
            var resultList = informedWorkflow.RunInformedWorkflow(lipidTarget, 30, 500);

            foreach (var result in resultList.OrderByDescending(x => x.GetNumMatchingMsMsPeaks()))
            {
                Console.WriteLine(result.Xic.GetApexScanNum() + " - " + result.HcdSpectrum.ScanNum + " - " + result.CidSpectrum.ScanNum + " - " + result.GetNumMatchingMsMsPeaks());
            }
        }
    }
}
