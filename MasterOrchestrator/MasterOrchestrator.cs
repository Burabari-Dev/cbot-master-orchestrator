using System;
using cAlgo.API;
using cAlgo.API.Collections;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots;

[Robot(AccessRights = AccessRights.None, AddIndicators = true)]
public class MasterOrchestrator : Robot
{
    [Parameter(DefaultValue = "Hello world!")]
    public string Message { get; set; }

    protected override void OnStart()
    {
        // To learn more about cTrader Algo visit our Help Center:
        // https://help.ctrader.com/ctrader-algo/

        Print(Message);
    }

    protected override void OnTick()
    {
        // Handle price updates here
    }

    protected override void OnStop()
    {
        // Handle cBot stop here
    }
}