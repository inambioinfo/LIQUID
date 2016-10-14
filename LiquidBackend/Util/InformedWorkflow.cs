﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using InformedProteomics.Backend.Data.Biology;
using InformedProteomics.Backend.Data.Composition;
using InformedProteomics.Backend.Data.Sequence;
using InformedProteomics.Backend.Data.Spectrometry;
using InformedProteomics.Backend.MassSpecData;
using LiquidBackend.Domain;

namespace LiquidBackend.Util
{
	public class InformedWorkflow
	{
		public LcMsRun LcMsRun { get; private set; }

		public InformedWorkflow(string rawFileLocation)
		{
			this.LcMsRun = LcMsDataFactory.GetLcMsData(rawFileLocation);
		}

		public List<SpectrumSearchResult> RunInformedWorkflow(LipidTarget target, double hcdMassError, double cidMassError)
		{
			return RunInformedWorkflow(target, this.LcMsRun, hcdMassError, cidMassError);
		}

		public static List<SpectrumSearchResult> RunInformedWorkflow(LipidTarget target, LcMsRun lcmsRun, double hcdMassError, double cidMassError)
		{
			IEnumerable<MsMsSearchUnit> msMsSearchUnits = target.GetMsMsSearchUnits();

			// I have to subtract an H for the target Ion since InformedProteomics will assume protenated
			var targetIon = new Ion(target.Composition - Composition.Hydrogen, 1);
		    double targetMz = target.MzRounded;
			Tolerance hcdTolerance = new Tolerance(hcdMassError, ToleranceUnit.Ppm);
			Tolerance cidTolerance = new Tolerance(cidMassError, ToleranceUnit.Ppm);

			// Find out which MS/MS scans have a precursor m/z that matches the target
			//List<int> matchingMsMsScanNumbers = lcmsRun.GetFragmentationSpectraScanNums(targetIon).ToList();
            List<int> matchingMsMsScanNumbers = lcmsRun.GetFragmentationSpectraScanNums(targetMz).ToList();

			List<SpectrumSearchResult> spectrumSearchResultList = new List<SpectrumSearchResult>();

			for (int i = 0; i+1 < matchingMsMsScanNumbers.Count; i+=2)
			{
				int firstScanNumber = matchingMsMsScanNumbers[i];
				int secondScanNumber = matchingMsMsScanNumbers[i+1];

				// Scan numbers should be consecutive.
				if (secondScanNumber - firstScanNumber != 1)
				{
					i--;
					continue;
				}

				// Lookup the MS/MS Spectrum
				ProductSpectrum firstMsMsSpectrum = lcmsRun.GetSpectrum(firstScanNumber) as ProductSpectrum;
				if (firstMsMsSpectrum == null) continue;

				// Lookup the MS/MS Spectrum
				ProductSpectrum secondMsMsSpectrum = lcmsRun.GetSpectrum(secondScanNumber) as ProductSpectrum;
				if (secondMsMsSpectrum == null) continue;

				// Filter MS/MS Spectrum based on mass error
				double msMsPrecursorMz = firstMsMsSpectrum.IsolationWindow.IsolationWindowTargetMz;
				//if (Math.Abs(msMsPrecursorMz - targetMz) > 0.4) continue;
				double ppmError = LipidUtil.PpmError(targetMz, msMsPrecursorMz);
				if (Math.Abs(ppmError) > hcdMassError) continue;

				// No need to move on if no MS1 data is found
				//if (!lcmsRun.CheckMs1Signature(targetIon, firstScanNumber, hcdTolerance)) continue;
				var precursor = lcmsRun.GetSpectrum(lcmsRun.GetPrecursorScanNum(firstScanNumber));
				var next = lcmsRun.GetSpectrum(lcmsRun.GetNextScanNum(firstScanNumber, 1));
				if (!precursor.ContainsIon(targetIon, hcdTolerance, .1) && !next.ContainsIon(targetIon, hcdTolerance, .1)) continue;

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

				// Get all matching peaks
				List<MsMsSearchResult> hcdSearchResultList = (from msMsSearchUnit in msMsSearchUnits let peak = hcdSpectrum.FindPeak(msMsSearchUnit.Mz, hcdTolerance) select new MsMsSearchResult(msMsSearchUnit, peak)).ToList();
				List<MsMsSearchResult> cidSearchResultList = (from msMsSearchUnit in msMsSearchUnits let peak = cidSpectrum.FindPeak(msMsSearchUnit.Mz, cidTolerance) select new MsMsSearchResult(msMsSearchUnit, peak)).ToList();

				// Find the MS1 data
				//Xic xic = lcmsRun.GetPrecursorExtractedIonChromatogram(targetMz, hcdTolerance, firstScanNumber);
				Xic xic = lcmsRun.GetFullPrecursorIonExtractedIonChromatogram(targetMz, hcdTolerance);

				// Bogus data
				if (xic.GetApexScanNum() < 0) continue;

				int precursorScanNumber = lcmsRun.GetPrecursorScanNum(firstScanNumber);
				Spectrum precursorSpectrum = lcmsRun.GetSpectrum(precursorScanNumber);

				SpectrumSearchResult spectrumSearchResult = new SpectrumSearchResult(hcdSpectrum, cidSpectrum, precursorSpectrum, hcdSearchResultList, cidSearchResultList, xic, lcmsRun)
				{
                    PrecursorTolerance = new Tolerance(hcdMassError, ToleranceUnit.Ppm)
				};
				spectrumSearchResultList.Add(spectrumSearchResult);
			}

			return spectrumSearchResultList;
		}

        public static List<SpectrumSearchResult> RunFragmentWorkflow(ICollection<MsMsSearchUnit> fragments, LcMsRun lcmsRun, double hcdMassError, double cidMassError, int minMatches, IProgress<int> progress = null )
        {
            IEnumerable<MsMsSearchUnit> PISearchUnits = fragments.Where(x => x.Description.Equals("Primary Ion"));
            Tolerance hcdTolerance = new Tolerance(hcdMassError, ToleranceUnit.Ppm);
            Tolerance cidTolerance = new Tolerance(cidMassError, ToleranceUnit.Ppm);
            List<int> scanTracker = new List<int>(); //track what scans have been included in spectrumSearchResultsList so we don't make duplicate entries for matched CID and HCD

            // Find all MS/MS scans            
            var msmsScanNumers = lcmsRun.GetScanNumbers(2);
            List<SpectrumSearchResult> spectrumSearchResultList = new List<SpectrumSearchResult>();
            var maxScans = msmsScanNumers.Count;

            foreach(int scan in msmsScanNumers)
            {
                // Lookup the MS/MS Spectrum
                ProductSpectrum MsMsSpectrum = lcmsRun.GetSpectrum(scan) as ProductSpectrum;
                ProductSpectrum MatchedSpectrum = null;
                var spectrum1 = lcmsRun.GetSpectrum(scan + 1);
                var spectrum2 = lcmsRun.GetSpectrum(scan - 1);
                if (spectrum1 != null && spectrum1.MsLevel == 2)
                {
                    if ((spectrum1 as ProductSpectrum).IsolationWindow.IsolationWindowTargetMz ==
                        MsMsSpectrum.IsolationWindow.IsolationWindowTargetMz)
                    {
                        MatchedSpectrum = spectrum1 as ProductSpectrum;
                    }
                }
                if (spectrum2 != null && spectrum2.MsLevel == 2)
                {
                    if ((spectrum2 as ProductSpectrum).IsolationWindow.IsolationWindowTargetMz ==
                        MsMsSpectrum.IsolationWindow.IsolationWindowTargetMz)
                    {
                        MatchedSpectrum = spectrum2 as ProductSpectrum;
                    }
                }

                if (MsMsSpectrum == null) continue;
                if (scanTracker.Contains(MsMsSpectrum.ScanNum)) continue;

                double msmsPrecursorMz = MsMsSpectrum.IsolationWindow.IsolationWindowTargetMz;
                int msmsPrecursorScan = lcmsRun.GetPrecursorScanNum(scan);

                Xic xic = lcmsRun.GetFullPrecursorIonExtractedIonChromatogram(msmsPrecursorMz, hcdTolerance);

                // Bogus data
                //if (xic.GetApexScanNum() < 0) continue;

                Spectrum precursorSpectrum = lcmsRun.GetSpectrum(msmsPrecursorScan);

                // Get all matching peaks
                List<MsMsSearchResult> SearchResultList = new List<MsMsSearchResult>();
                //IEnumerable<MsMsSearchUnit> NLSearchUnits = fragments.Where(x=> x.Description.Equals("Neutral Loss")).Select(x => {x.Mz = (msmsPrecursorMz - x.Mz); return x;});
                IEnumerable<MsMsSearchUnit> NLSearchUnits = fragments.Where(x => x.Description.Equals("Neutral Loss")).Select(y => new MsMsSearchUnit(msmsPrecursorMz-y.Mz,"Neutral Loss"));
                IEnumerable<MsMsSearchUnit> MsMsSearchUnits = PISearchUnits.Concat(NLSearchUnits);
                SpectrumSearchResult spectrumSearchResult = null;


                ProductSpectrum hcdSpectrum = MsMsSpectrum.ActivationMethod == ActivationMethod.HCD ? MsMsSpectrum : MatchedSpectrum;
                ProductSpectrum cidSpectrum = MsMsSpectrum.ActivationMethod == ActivationMethod.CID ? MsMsSpectrum : MatchedSpectrum;


                var HcdSearchResultList = hcdSpectrum != null? (from msMsSearchUnit in MsMsSearchUnits
                                            let peak = hcdSpectrum.FindPeak(msMsSearchUnit.Mz, hcdTolerance)
                                            select new MsMsSearchResult(msMsSearchUnit, peak)).ToList() : new List<MsMsSearchResult>();
                var CidSearchResultList = cidSpectrum != null? (from msMsSearchUnit in MsMsSearchUnits
                                            let peak = cidSpectrum.FindPeak(msMsSearchUnit.Mz, cidTolerance)
                                            select new MsMsSearchResult(msMsSearchUnit, peak)).ToList() : new List<MsMsSearchResult>();
                SearchResultList = HcdSearchResultList.Concat(CidSearchResultList).ToList();
                if (precursorSpectrum != null)
                {
                    spectrumSearchResult = new SpectrumSearchResult(hcdSpectrum, cidSpectrum, precursorSpectrum,
                        HcdSearchResultList, CidSearchResultList, xic, lcmsRun)
                    {
                        PrecursorTolerance = new Tolerance(hcdMassError, ToleranceUnit.Ppm)
                    };
                }
                else
                {
                    spectrumSearchResult = new SpectrumSearchResult(hcdSpectrum, cidSpectrum, HcdSearchResultList, CidSearchResultList, lcmsRun)
                    {
                        PrecursorTolerance = new Tolerance(hcdMassError, ToleranceUnit.Ppm)
                    };
                }

                if(hcdSpectrum != null) scanTracker.Add(hcdSpectrum.ScanNum);
                if(cidSpectrum != null) scanTracker.Add(cidSpectrum.ScanNum);

                if (SearchResultList.Count(x => x.ObservedPeak != null) < minMatches) continue;
                spectrumSearchResultList.Add(spectrumSearchResult);

                // Report progress
                if (progress != null)
                {
                    int currentProgress = (int)(((double)scan / maxScans) * 100);
                    progress.Report(currentProgress);
                }
            }

            return spectrumSearchResultList;
        }
	}
}
