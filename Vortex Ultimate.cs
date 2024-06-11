// -------------------------------------------------------------------------------
// A classic Vortex indicator with extra features:
//  * MTF support (higher timeframe data on a lower timeframe)
//  * Smoothing (simple, exponential, TEMA)
//  * Alert system (cross, reversal)
//   
//   Version 1.00
//   Copyright 2024, EarnForex.com
//   https://www.earnforex.com/metatrader-indicators/Vortex-Ultimate/
// -------------------------------------------------------------------------------

using cAlgo.API;
using cAlgo.API.Internals;
using System;

namespace cAlgo
{
    [Indicator(AccessRights = AccessRights.None)]
    public class VortexUltimate : Indicator
    {
        // Enumeration for alert candle:
        public enum ENUM_ALERT_CANDLE
        {
            PREVIOUS_CANDLE, // Previous
            CURRENT_CANDLE // Current
        };

        // Enumeration for smoothing types:
        public enum ENUM_SMOOTHING_TYPE
        {
            SIMPLE, // Simple
            EXPONENTIAL, // Exponential
            TEMA // Triple exponential (slowest)
        };

        // Enumeration for alert types:
        public enum ENUM_ALERT_TYPE
        {
            CROSS, // Alert when VI+ crosses VI-
            REVERSAL, // Alert when VI+ and VI- reverse
            BOTH // Alert on both signals
        };

        // For reversal alerts. Can be either widening, neutral, or tightening.
        public enum ENUM_REVERSAL_STATE
        {
            NEUTRAL, // Neutral
            WIDENING, // Widening
            TIGHTENING // Tightening
        };

        [Parameter("Periods", DefaultValue = 14, Group = "Main")]
        public int IndPeriod { get; set; }

        [Parameter("Smoothing", DefaultValue = 0, Group = "Main")]
        public int Smoothing { get; set; }

        [Parameter("Type Smoothing", DefaultValue = ENUM_SMOOTHING_TYPE.SIMPLE, Group = "Main")]
        public ENUM_SMOOTHING_TYPE TypeSmoothing { get; set; }

        [Parameter("Timeframe", Group = "Main")]
        public TimeFrame InpTimeframe { get; set; }

        [Parameter("Alert Type", DefaultValue = ENUM_ALERT_TYPE.BOTH, Group = "Alerts")]
        public ENUM_ALERT_TYPE AlertType { get; set; }
        
        [Parameter("Alert Candle", DefaultValue  = ENUM_ALERT_CANDLE.PREVIOUS_CANDLE, Group = "Alerts")]
        public ENUM_ALERT_CANDLE AlertCandle { get; set; }

        [Parameter("Show Alert", DefaultValue = false, Group = "Alerts")]
        public bool IsShowAlert { get; set; }

        [Parameter("Send Email", DefaultValue = false, Group = "Alerts")]
        public bool IsSendEmail { get; set; }

        [Parameter("Sender Email", Group = "Alerts")]
        public string SenderEmail { get; set; }
        
        [Parameter("Receiver Email", Group = "Alerts")]
        public string ReceiverEmail { get; set; }

        [Output("VI+", LineColor = "Blue", LineStyle = LineStyle.Solid, Thickness = 2)]
        public IndicatorDataSeries PlusVI { get; set; }  // VI+: Vortex Indicator+

        [Output("VI-", LineColor = "Red", LineStyle = LineStyle.Solid, Thickness = 2)]
        public IndicatorDataSeries MinusVI { get; set; }   // VI-: Vortex Indicator-


        public IndicatorDataSeries prePlusVI;       // Pre-Smoothed Vortex Indicator+ when smoothing is enabled.
        public IndicatorDataSeries preMinusVI;      // Pre-Smoothed Vortex Indicator- when smoothing is enabled.

        public IndicatorDataSeries TEMA_1_PlusVI;   // First pass of exponentiation for TEMA smoothing for VI+.
        public IndicatorDataSeries TEMA_1_MinusVI;  // First pass of exponentiation for TEMA smoothing for VI-.

        public IndicatorDataSeries TEMA_2_PlusVI;   // Second pass of exponentiation for TEMA smoothing for VI+.
        public IndicatorDataSeries TEMA_2_MinusVI;  // Second pass of exponentiation for TEMA smoothing for VI-.

        public IndicatorDataSeries TEMA_3_PlusVI;   // Third pass of exponentiation for TEMA smoothing for VI+.
        public IndicatorDataSeries TEMA_3_MinusVI;  // Third pass of exponentiation for TEMA smoothing for VI-.

        public TimeFrame Timeframe; // Timeframe of operation.
        double alpha;
        ENUM_REVERSAL_STATE CurrentState = ENUM_REVERSAL_STATE.NEUTRAL;
        int prevhtfindex = -1;
        int LastAlertBar = -1;
        string LastAlertType = "";

        protected override void Initialize()
        {
            CurrentState = ENUM_REVERSAL_STATE.NEUTRAL;

            if (InpTimeframe < TimeFrame) Timeframe = TimeFrame; // If upper timeframe is actually set to a lower one, don't use it.
            else Timeframe = InpTimeframe;
            
            alpha = 2.0 / (Smoothing + 1.0);

            prePlusVI = CreateDataSeries();
            preMinusVI = CreateDataSeries();
            TEMA_1_PlusVI = CreateDataSeries();
            TEMA_1_MinusVI = CreateDataSeries();
            TEMA_2_PlusVI = CreateDataSeries();
            TEMA_2_MinusVI = CreateDataSeries();
            TEMA_3_PlusVI = CreateDataSeries();
            TEMA_3_MinusVI = CreateDataSeries();
        }
        
        // Returns lower timeframe index.
        public int GetLTF(int idx)
        {
            return Bars.OpenTimes.GetIndexByTime(MarketData.GetBars(Timeframe).OpenTimes[idx]);
        }

        public override void Calculate(int index)
        {
            int i = index;
            int htfindex = MarketData.GetBars(Timeframe).OpenTimes.GetIndexByTime(Bars.OpenTimes[index]);
            if (Timeframe > TimeFrame) // No need to repeat the same calculations for the bars within the same upper timeframe bar in MTF mode.
            {
                if ((!IsLastBar) &&  (htfindex == prevhtfindex))
                {
                    PlusVI[index] = PlusVI[index - 1];
                    MinusVI[index] = MinusVI[index - 1];
                    prePlusVI[index] = prePlusVI[index - 1];
                    preMinusVI[index] = preMinusVI[index - 1];
                    TEMA_1_PlusVI[index] = TEMA_1_PlusVI[index - 1];
                    TEMA_1_MinusVI[index] = TEMA_1_MinusVI[index - 1];
                    TEMA_2_PlusVI[index] = TEMA_2_PlusVI[index - 1];
                    TEMA_2_MinusVI[index] = TEMA_2_MinusVI[index - 1];
                    TEMA_3_PlusVI[index] = TEMA_3_PlusVI[index - 1];
                    TEMA_3_MinusVI[index] = TEMA_3_MinusVI[index - 1];
                    return;
                }
                i = htfindex;
            }

            Bars barsTF = MarketData.GetBars(Timeframe); ;

            var startMainIndex = index;
            if ((IsLastBar) && (Timeframe > TimeFrame)) // Need to update last values according to the High TF Bar that hasn't closed yet.
            {
                if (prevhtfindex == -1) prevhtfindex = htfindex;
                startMainIndex = Bars.OpenTimes.GetIndexByTime(barsTF.OpenTimes[prevhtfindex]);
            }
            
            for (int k = startMainIndex; k <= index; k++) // Potential recalculation for MTF. In non-MTF, will run only once.
            {
                double SumPlusVM = 0;
                double SumMinusVM = 0;
                double SumTR = 0;
                for (int j = 0; j < IndPeriod; j++)
                {
                    double H = barsTF.HighPrices[i - j];
                    double L = barsTF.LowPrices[i - j];
                    double C_P = barsTF.ClosePrices[i - j - 1];
                    SumPlusVM += Math.Abs(H - barsTF.LowPrices[i - j - 1]);
                    SumMinusVM += Math.Abs(L - barsTF.HighPrices[i - j - 1]);
                    SumTR += Math.Max(H, C_P) - Math.Min(L, C_P);
                }
                if (Smoothing > 0) // Use different arrays to store raw values when smoothing is used.
                {
                    prePlusVI[k] = SumPlusVM / SumTR;
                    preMinusVI[k] = SumMinusVM / SumTR;
                }
                else
                {
                    PlusVI[k] = SumPlusVM / SumTR;
                    MinusVI[k] = SumMinusVM / SumTR;
                }
                
                if (Smoothing > 0)
                {
                    if (TypeSmoothing == ENUM_SMOOTHING_TYPE.SIMPLE) // Simple
                    {
                        if (((Timeframe <= TimeFrame) && (k >= IndPeriod + Smoothing)) || // Non-MTF
                            ((Timeframe >  TimeFrame) && (UpperTFShift(k) - UpperTFShift(0) >= IndPeriod + Smoothing))) // MTF
                        { 
                            DoSmoothing(k);
                        }
                    }
                    else // Exponential or TEMA.
                    {
                        if (((Timeframe <= TimeFrame) && (k >= IndPeriod)) ||
                            ((Timeframe >  TimeFrame) && (UpperTFShift(k) - UpperTFShift(0) >= IndPeriod))) // MTF
                        {
                            DoSmoothing(k);
                        }
                    }
                }
            }
            if (IsLastBar) HandleAlerts(index, htfindex);
            prevhtfindex = htfindex;
        }

        public int UpperTFShift(int idx)
        {
            return MarketData.GetBars(Timeframe).OpenTimes.GetIndexByTime(Bars.OpenTimes[idx]);
        }

        public void DoSmoothing(int i)
        {
            double SumPlusVI = 0;
            double SumMinusVI = 0;

            if (Timeframe > TimeFrame) // MTF
            {
                if (TypeSmoothing == ENUM_SMOOTHING_TYPE.SIMPLE)
                {
                    if (UpperTFShift(i) - UpperTFShift(0) == IndPeriod + Smoothing) // First smoothed element.
                    {
                        for (int j = 0, k = 0; k < Smoothing; k++, j++)
                        {
                            SumPlusVI += prePlusVI[i - j];
                            SumMinusVI += preMinusVI[i - j];
                            while (UpperTFShift(i - j - 1) == UpperTFShift(i - j))
                            {
                                j++;
                            }
                        }
                        PlusVI[i] = SumPlusVI / Smoothing;
                        MinusVI[i] = SumMinusVI / Smoothing;
                    }
                    else // Next smoothed elements can be calculated using the previous ones.
                    {
                        int j = 1;
                        // Find previous upper timeframe PlusVI.
                        while (UpperTFShift(i - j) == UpperTFShift(i))
                        {
                            j++;
                        }

                        int prev_j = j;
                        // Find last upper timeframe prePlusVI (the one that's out of the moving average calculation at this point).
                        // Do it by finding N changes in the upper timeframe bar numbers. This helps to avoid issues when there is a hole in lower timeframe history.
                        int count = 0;
                        while (count < Smoothing)
                        {
                            if (UpperTFShift(i - j + 1) != UpperTFShift(i - j)) count++;
                            j++;
                        }

                        PlusVI[i] = PlusVI[i - prev_j] + (prePlusVI[i] - prePlusVI[i - j]) / Smoothing;
                        MinusVI[i] = MinusVI[i - prev_j] + (preMinusVI[i] - preMinusVI[i - j]) / Smoothing;
                    }
                }
                else if (TypeSmoothing == ENUM_SMOOTHING_TYPE.EXPONENTIAL)
                {
                    if (UpperTFShift(i) - UpperTFShift(0) == IndPeriod) // First smoothed element.
                    {
                        PlusVI[i] = prePlusVI[i];
                        MinusVI[i] = preMinusVI[i];
                    }
                    else // Next smoothed elements can be calculated using the previous ones.
                    {
                        int j = 1;
                        // Find previous upper timeframe PlusVI.
                        while (UpperTFShift(i - j) == UpperTFShift(i))
                        {
                            j++;
                        }
                        PlusVI[i] = alpha * prePlusVI[i] + (1 - alpha) * PlusVI[i - j];
                        MinusVI[i] = alpha * preMinusVI[i] + (1 - alpha) * MinusVI[i - j];
                    }
                }
                else if (TypeSmoothing == ENUM_SMOOTHING_TYPE.TEMA)
                {
                    if (UpperTFShift(i) - UpperTFShift(0) == IndPeriod) // First element.
                    {
                        TEMA_1_PlusVI[i] = prePlusVI[i];
                        TEMA_1_MinusVI[i] = preMinusVI[i];
                        TEMA_2_PlusVI[i] = prePlusVI[i];
                        TEMA_2_MinusVI[i] = preMinusVI[i];
                        TEMA_3_PlusVI[i] = prePlusVI[i];
                        TEMA_3_MinusVI[i] = preMinusVI[i];
                        PlusVI[i] = prePlusVI[i];
                        MinusVI[i] = preMinusVI[i];
                    }
                    int j = 1;
                    // Find previous upper timeframe PlusVI.
                    while (UpperTFShift(i - j) == UpperTFShift(i))
                    {
                        j++;
                    }
                    if (UpperTFShift(i) - UpperTFShift(0) > IndPeriod)
                    {
                        TEMA_1_PlusVI[i] = alpha * prePlusVI[i] + (1 - alpha) * TEMA_1_PlusVI[i - j];
                        TEMA_1_MinusVI[i] = alpha * preMinusVI[i] + (1 - alpha) * TEMA_1_MinusVI[i - j];
                        if (UpperTFShift(i) - UpperTFShift(0) == IndPeriod + 1) // Second element.
                        {
                            TEMA_2_PlusVI[i] = TEMA_1_PlusVI[i];
                            TEMA_2_MinusVI[i] = TEMA_1_MinusVI[i];
                            TEMA_3_PlusVI[i] = TEMA_1_PlusVI[i];
                            TEMA_3_MinusVI[i] = TEMA_1_MinusVI[i];
                            PlusVI[i] = TEMA_1_PlusVI[i];
                            MinusVI[i] = TEMA_1_MinusVI[i];
                        }
                    }
                    if (UpperTFShift(i) - UpperTFShift(0) > IndPeriod + 1)
                    {
                        TEMA_2_PlusVI[i] = alpha * TEMA_1_PlusVI[i] + (1 - alpha) * TEMA_2_PlusVI[i - j];
                        TEMA_2_MinusVI[i] = alpha * TEMA_1_MinusVI[i] + (1 - alpha) * TEMA_2_MinusVI[i - j];
                        if (UpperTFShift(i) - UpperTFShift(0) == IndPeriod + 2) // Third element.
                        {
                            TEMA_3_PlusVI[i] = TEMA_2_PlusVI[i];
                            TEMA_3_MinusVI[i] = TEMA_2_MinusVI[i];
                            PlusVI[i] = TEMA_2_PlusVI[i];
                            MinusVI[i] = TEMA_2_MinusVI[i];
                        }
                    }

                    if (UpperTFShift(i) - UpperTFShift(0) > IndPeriod + 2)
                    {
                        TEMA_3_PlusVI[i] = alpha * TEMA_2_PlusVI[i] + (1 - alpha) * TEMA_3_PlusVI[i - j];
                        TEMA_3_MinusVI[i] = alpha * TEMA_2_MinusVI[i] + (1 - alpha) * TEMA_3_MinusVI[i - j];
                        if (UpperTFShift(i) - UpperTFShift(0) == IndPeriod + 3) // Fourth element.
                        {
                            PlusVI[i] = TEMA_3_PlusVI[i];
                            MinusVI[i] = TEMA_3_MinusVI[i];
                        }
                    }
                    if (UpperTFShift(i) - UpperTFShift(0) > IndPeriod + 3)
                    {
                        PlusVI[i] = 3 * TEMA_1_PlusVI[i] - 3 * TEMA_2_PlusVI[i] + TEMA_3_PlusVI[i];
                        MinusVI[i] = 3 * TEMA_1_MinusVI[i] - 3 * TEMA_2_MinusVI[i] + TEMA_3_MinusVI[i];
                    }
                }
            }
            else // Non-MTF.
            {
                if (TypeSmoothing == ENUM_SMOOTHING_TYPE.SIMPLE)
                {
                    if (i == IndPeriod + Smoothing) // First smoothed element.
                    {
                        for (int j = 0; j < Smoothing; j++)
                        {
                            SumPlusVI += prePlusVI[i - j];
                            SumMinusVI += preMinusVI[i - j];
                        }
                        PlusVI[i] = SumPlusVI / Smoothing;
                        MinusVI[i] = SumMinusVI / Smoothing;
                    }
                    else // Next smoothed elements can be calculated using the previous ones.
                    {
                        PlusVI[i] = PlusVI[i - 1] + (prePlusVI[i] - prePlusVI[i - Smoothing]) / Smoothing;
                        MinusVI[i] = MinusVI[i - 1] + (preMinusVI[i] - preMinusVI[i - Smoothing]) / Smoothing;
                    }
                }
                else if (TypeSmoothing == ENUM_SMOOTHING_TYPE.EXPONENTIAL)
                {
                    if (i == IndPeriod) // First element.
                    {
                        PlusVI[i] = prePlusVI[i];
                        MinusVI[i] = preMinusVI[i];
                    }
                    else
                    {
                        PlusVI[i] = alpha * prePlusVI[i] + (1 - alpha) * PlusVI[i - 1];
                        MinusVI[i] = alpha * preMinusVI[i] + (1 - alpha) * MinusVI[i - 1];
                    }
                }
                else if (TypeSmoothing == ENUM_SMOOTHING_TYPE.TEMA)
                {
                    if (i == IndPeriod) // First element.
                    {
                        TEMA_1_PlusVI[i] = prePlusVI[i];
                        TEMA_1_MinusVI[i] = preMinusVI[i];
                        TEMA_2_PlusVI[i] = prePlusVI[i];
                        TEMA_2_MinusVI[i] = preMinusVI[i];
                        TEMA_3_PlusVI[i] = prePlusVI[i];
                        TEMA_3_MinusVI[i] = preMinusVI[i];
                        PlusVI[i] = prePlusVI[i];
                        MinusVI[i] = preMinusVI[i];
                    }
                    if (i > IndPeriod)
                    {
                        TEMA_1_PlusVI[i] = alpha * prePlusVI[i] + (1 - alpha) * TEMA_1_PlusVI[i - 1];
                        TEMA_1_MinusVI[i] = alpha * preMinusVI[i] + (1 - alpha) * TEMA_1_MinusVI[i - 1];
                        if (i == IndPeriod + 1) // Second element.
                        {
                            TEMA_2_PlusVI[i] = TEMA_1_PlusVI[i];
                            TEMA_2_MinusVI[i] = TEMA_1_MinusVI[i];
                            TEMA_3_PlusVI[i] = TEMA_1_PlusVI[i];
                            TEMA_3_MinusVI[i] = TEMA_1_MinusVI[i];
                            PlusVI[i] = TEMA_1_PlusVI[i];
                            MinusVI[i] = TEMA_1_MinusVI[i];
                        }
                    }
                    if (i > IndPeriod + 1)
                    {
                        TEMA_2_PlusVI[i] = alpha * TEMA_1_PlusVI[i] + (1 - alpha) * TEMA_2_PlusVI[i - 1];
                        TEMA_2_MinusVI[i] = alpha * TEMA_1_MinusVI[i] + (1 - alpha) * TEMA_2_MinusVI[i - 1];
                        if (i == IndPeriod + 2) // Third element.
                        {
                            TEMA_3_PlusVI[i] = TEMA_2_PlusVI[i];
                            TEMA_3_MinusVI[i] = TEMA_2_MinusVI[i];
                            PlusVI[i] = TEMA_2_PlusVI[i];
                            MinusVI[i] = TEMA_2_MinusVI[i];
                        }
                    }
                    if (i > IndPeriod + 2)
                    {
                        TEMA_3_PlusVI[i] = alpha * TEMA_2_PlusVI[i] + (1 - alpha) * TEMA_3_PlusVI[i - 1];
                        TEMA_3_MinusVI[i] = alpha * TEMA_2_MinusVI[i] + (1 - alpha) * TEMA_3_MinusVI[i - 1];
                        if (i == IndPeriod + 3) // Fourth element.
                        {
                            PlusVI[i] = TEMA_3_PlusVI[i];
                            MinusVI[i] = TEMA_3_MinusVI[i];
                        }
                    }
                    if (i > IndPeriod + 3)
                    {
                        PlusVI[i] = 3 * TEMA_1_PlusVI[i] - 3 * TEMA_2_PlusVI[i] + TEMA_3_PlusVI[i];
                        MinusVI[i] = 3 * TEMA_1_MinusVI[i] - 3 * TEMA_2_MinusVI[i] + TEMA_3_MinusVI[i];
                    }
                }
            }
        }

        public void HandleAlerts(int idx, int htf)
        {
            if ((!IsShowAlert) && (!IsSendEmail)) return;

            int shift;
            if (Timeframe <= TimeFrame) // Normal:
                shift = AlertCandle == ENUM_ALERT_CANDLE.PREVIOUS_CANDLE ? idx - 1 : idx;
            else // MTF:
                shift = AlertCandle == ENUM_ALERT_CANDLE.PREVIOUS_CANDLE ? GetLTF(htf - 1) : GetLTF(htf);

            int shift_prev = shift - 1;

            if (AlertType == ENUM_ALERT_TYPE.BOTH || AlertType == ENUM_ALERT_TYPE.CROSS)
            {
                if (HasBearishCrossing(shift, shift_prev))
                    Notify("Bearish Crossing", htf);

                if (HasBullishCrossing(shift, shift_prev))
                    Notify("Bullish Crossing", htf);
            }
            if (AlertType == ENUM_ALERT_TYPE.BOTH || AlertType == ENUM_ALERT_TYPE.REVERSAL)
            {
                if (HasBearishReversal(shift, shift_prev))
                    Notify("Bearish Reversal", htf);

                if (HasBullishReversal(shift, shift_prev))
                    Notify("Bullish Reversal", htf);
            }
        }

        public bool HasBearishCrossing(int shift, int shift_prev)
        {
            if ((PlusVI[shift] < MinusVI[shift]) && (PlusVI[shift_prev] > MinusVI[shift_prev])) return true;
            return false;
        }
        
        public bool HasBullishCrossing(int shift, int shift_prev)
        {
            if ((PlusVI[shift] > MinusVI[shift]) && (PlusVI[shift_prev] < MinusVI[shift_prev])) return true;
            return false;
        }
        
        public bool HasBearishReversal(int shift, int shift_prev)
        {
            if (CurrentState == ENUM_REVERSAL_STATE.NEUTRAL) // First time.
            {
                if (PlusVI[shift] - MinusVI[shift] > PlusVI[shift_prev] - MinusVI[shift_prev]) CurrentState = ENUM_REVERSAL_STATE.TIGHTENING; // Tightened.
                else if (PlusVI[shift] - MinusVI[shift] < PlusVI[shift_prev] - MinusVI[shift_prev]) CurrentState = ENUM_REVERSAL_STATE.WIDENING; // Widened.
                return false;
            }
            if (CurrentState == ENUM_REVERSAL_STATE.TIGHTENING)
            {
                if (PlusVI[shift] - MinusVI[shift] < PlusVI[shift_prev] - MinusVI[shift_prev])
                {
                    CurrentState = ENUM_REVERSAL_STATE.WIDENING; // Widened.
                    return true;
                }
            }
            return false;
        }

        public bool HasBullishReversal(int shift, int shift_prev)
        {
            if (CurrentState == ENUM_REVERSAL_STATE.NEUTRAL) // First time.
            {
                if (PlusVI[shift] - MinusVI[shift] > PlusVI[shift_prev] - MinusVI[shift_prev]) CurrentState = ENUM_REVERSAL_STATE.TIGHTENING; // Tightened.
                else if (PlusVI[shift] - MinusVI[shift] < PlusVI[shift_prev] - MinusVI[shift_prev]) CurrentState = ENUM_REVERSAL_STATE.WIDENING; // Widened.
                return false;
            }
            if (CurrentState == ENUM_REVERSAL_STATE.WIDENING)
            {
                if (PlusVI[shift] - MinusVI[shift] > PlusVI[shift_prev] - MinusVI[shift_prev])
                {
                    CurrentState = ENUM_REVERSAL_STATE.TIGHTENING; // Tightened.
                    return true;
                }
            }
            return false;
        }

        private void Notify(string message, int htf)
        {
            if ((LastAlertBar == htf) && (LastAlertType == message)) return;
            LastAlertType = message;
            message += " - " + Symbol.ToString() + " @ " + TimeFrame.ToString();
            if (Timeframe > TimeFrame) message += " on " + Timeframe.ToString();

            if (IsShowAlert)
            {
                Notifications.PlaySound(SoundType.Doorbell);
                Print(message);
                //MessageBox.Show(message, "Vortex Ultimate", MessageBoxButton.OK); // Causes issues!
            }
            if ((IsSendEmail) && (!string.IsNullOrWhiteSpace(SenderEmail) && !string.IsNullOrWhiteSpace(ReceiverEmail)))
            {
                Notifications.SendEmail(SenderEmail, ReceiverEmail, "Vortex Ultimate Alert", message);
                Print(message);
            }
            LastAlertBar = htf;
        }
    }
}