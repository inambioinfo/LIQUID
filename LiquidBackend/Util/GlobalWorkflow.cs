﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using InformedProteomics.Backend.Data.Spectrometry;
using InformedProteomics.Backend.MassSpecData;
using LiquidBackend.Domain;

namespace LiquidBackend.Util
{
	public class GlobalWorkflow
	{
		public LcMsRun LcMsRun { get; private set; }

		public GlobalWorkflow(string rawFileLocation)
		{
			this.LcMsRun = LcMsRun.GetLcMsRun(rawFileLocation, MassSpecDataType.XCaliburRun);
		}

		public void RunGlobalWorkflow(IEnumerable<Lipid> lipidList, double hcdMassError, double cidMassError)
		{
			RunGlobalWorkflow(lipidList, this.LcMsRun, hcdMassError, cidMassError);
		}

		public static void RunGlobalWorkflow(IEnumerable<Lipid> lipidList, LcMsRun lcmsRun, double hcdMassError, double cidMassError)
		{
			//TextWriter textWriter = new StreamWriter("outputNeg.tsv");

			Tolerance hcdTolerance = new Tolerance(hcdMassError, ToleranceUnit.Ppm);
			Tolerance cidTolerance = new Tolerance(cidMassError, ToleranceUnit.Ppm);

			var lipidsGroupedByTarget = lipidList.OrderBy(x => x.LipidTarget.Composition.Mass).GroupBy(x => x.LipidTarget).ToList();

			int minLcScan = lcmsRun.MinLcScan;
			int maxLcScan = lcmsRun.MaxLcScan;

			for (int i = minLcScan; i <= maxLcScan; i++)
			{
				// Lookup the MS/MS Spectrum
				ProductSpectrum firstMsMsSpectrum = lcmsRun.GetSpectrum(i) as ProductSpectrum;
				if (firstMsMsSpectrum == null) continue;

				// Lookup the MS/MS Spectrum
				ProductSpectrum secondMsMsSpectrum = lcmsRun.GetSpectrum(i + 1) as ProductSpectrum;
				if (secondMsMsSpectrum == null) continue;

				//textWriter.WriteLine(i);
				//Console.WriteLine(DateTime.Now + "\tProcessing Scan" + i);

				// Grab Precursor Spectrum
				int precursorScanNumber = lcmsRun.GetPrecursorScanNum(i);
				Spectrum precursorSpectrum = lcmsRun.GetSpectrum(precursorScanNumber);

				// Assign each MS/MS spectrum to HCD or CID
				ProductSpectrum hcdSpectrum;
				ProductSpectrum cidSpectrum;
				if (firstMsMsSpectrum.ActivationMethod == ActivationMethod.HCD)
				{
					hcdSpectrum = firstMsMsSpectrum;
					cidSpectrum = secondMsMsSpectrum;
				}
				else
				{
					hcdSpectrum = secondMsMsSpectrum;
					cidSpectrum = firstMsMsSpectrum;
				}

				double msMsPrecursorMz = firstMsMsSpectrum.IsolationWindow.IsolationWindowTargetMz;
				double mzToSearchTolerance = hcdMassError * msMsPrecursorMz / 1000000;
				double lowMz = msMsPrecursorMz - mzToSearchTolerance;
				double highMz = msMsPrecursorMz + mzToSearchTolerance;

				Dictionary<IGrouping<LipidTarget, Lipid>, SpectrumSearchResult> resultDictionary = new Dictionary<IGrouping<LipidTarget, Lipid>, SpectrumSearchResult>();

				foreach (var lipidGrouping in lipidsGroupedByTarget)
				{
					LipidTarget lipidTarget = lipidGrouping.Key;
					double lipidMz = lipidTarget.Composition.Mass;

					// If we reached the point where the m/z is too high, we can exit
					if (lipidMz > highMz) break;

					if (lipidMz > lowMz)
					{
						// Find the MS1 data
						Xic xic = lcmsRun.GetExtractedIonChromatogram(lipidMz, hcdTolerance, i);

						// Bogus data
						if (xic.GetApexScanNum() < 0) continue;

						// Grab the MS/MS peak to search for
						IEnumerable<MsMsSearchUnit> msMsSearchUnits = lipidTarget.GetMsMsSearchUnits();

						// Get all matching peaks
						List<MsMsSearchResult> hcdSearchResultList = (from msMsSearchUnit in msMsSearchUnits let peak = hcdSpectrum.FindPeak(msMsSearchUnit.Mz, hcdTolerance) select new MsMsSearchResult(msMsSearchUnit, peak)).ToList();
						List<MsMsSearchResult> cidSearchResultList = (from msMsSearchUnit in msMsSearchUnits let peak = cidSpectrum.FindPeak(msMsSearchUnit.Mz, cidTolerance) select new MsMsSearchResult(msMsSearchUnit, peak)).ToList();

						// Create spectrum search results
						SpectrumSearchResult spectrumSearchResult = new SpectrumSearchResult(hcdSpectrum, cidSpectrum, precursorSpectrum, hcdSearchResultList, cidSearchResultList, xic);
						resultDictionary.Add(lipidGrouping, spectrumSearchResult);

						//textWriter.WriteLine(lipidTarget.CommonName + "\t" + spectrumSearchResult.Score);
						//Console.WriteLine(lipidTarget.CommonName + "\t" + spectrumSearchResult.Score);
					}
				}

				// Skip an extra scan since we look at 2 at a time
				i++;
			}

			//textWriter.Close();
		}

		public void RunGlobalWorkflowSingleScan()
		{
			
		}
	}
}