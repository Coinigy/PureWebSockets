# PureWebSockets
**A Cross Platform WebSocket Client for .NET Core NetStandard**

**[NuGet Package](https://www.nuget.org/packages/PureWebSockets)** [![PureWebSockets](https://img.shields.io/nuget/v/PureWebSockets.svg)](https://www.nuget.org/packages/PureWebSockets/) 

[![Codacy Badge](https://api.codacy.com/project/badge/Grade/29c72c135c094441a15137cba33f8e49)](https://app.codacy.com/app/ByronAP/PureWebSockets?utm_source=github.com&utm_medium=referral&utm_content=Coinigy/PureWebSockets&utm_campaign=Badge_Grade_Settings)
[![Build Status](https://dev.azure.com/byronap/PuewWebSockets/_apis/build/status/Coinigy.PureWebSockets?branchName=master)](https://dev.azure.com/byronap/PuewWebSockets/_build/latest?definitionId=1&branchName=master)

##### Requirements
* .NET NetStandard V2.0+

##### Usage
* Example included in project
  
  Provided by: 2018 -2019 Coinigy Inc. Coinigy.com

## V3 Breaking Changes
* Events now have a sender object which is the instance that reaised the event.
* (non breaking but useful) An addition construstor has been added with an instance name argument which makes it easier to identify an instance when using multiple connections. This links to the new property InstanceName.
