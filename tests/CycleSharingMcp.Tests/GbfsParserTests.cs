using CycleSharingMcp.Core.Gbfs;

namespace CycleSharingMcp.Tests;

public class GbfsParserTests
{
    [Fact]
    public void Docomo_StationInformation()
    {
        var ports = GbfsParser.ParseStationInformation(Fixtures.Read("docomo_station_information.json"), Operator.Docomo);
        Assert.Equal(3, ports.Count);
        var p = ports.Single(x => x.Id.StationId == "5053");
        Assert.Equal((new PortId(Operator.Docomo, "5053"), "M05.塩見縄手", (int?)6, "2", (string?)null),
                     (p.Id, p.Name, p.Capacity, p.RegionId, p.Address));
        Assert.Equal((35.478819, 133.04884), (p.Lat, p.Lon));
        Assert.Equal("m05.塩見縄手", p.SearchName);
    }

    [Fact]
    public void Hello_StationInformation_ReadsExtrasAndToleratesMissing()
    {
        var ports = GbfsParser.ParseStationInformation(Fixtures.Read("hellocycling_station_information.json"), Operator.HelloCycling);
        var p17 = ports.Single(x => x.Id.StationId == "17");
        Assert.Equal(8, p17.Capacity);
        Assert.Equal("東京都台東区台東４丁目３１－１", p17.Address);
        Assert.Equal("東京都台東区台東4-31-1", p17.SearchAddress);
        Assert.Equal("https://www.hellocycling.jp/app/port/detail/17?referrer=odpt", p17.RentalUrl);
        Assert.Equal((false, "street_parking"), (p17.IsChargingStation, p17.ParkingType));
        Assert.Null(p17.RegionId);

        var p300 = ports.Single(x => x.Id.StationId == "300");
        Assert.Null(p300.Capacity);
        Assert.Null(p300.Address);
        Assert.Null(p300.SearchAddress);
        Assert.Null(p300.RentalUrl);
    }

    [Fact]
    public void StationWithoutCoordinates_IsSkipped()
    {
        // 座標のないポートを (0, 0) として扱うと、近傍検索で実在しない位置に出てしまう
        var json = """{"data":{"stations":[{"station_id":"1","name":"a"},{"station_id":"2","name":"b","lat":35.0},{"station_id":"3","name":"c","lat":35.0,"lon":139.0}]}}"""u8;

        Assert.Equal(["3"], GbfsParser.ParseStationInformation(json, Operator.Docomo).Select(p => p.Id.StationId));
    }

    [Fact]
    public void NonObjectArrayElements_AreSkipped()
    {
        var info = """{"data":{"stations":[1,"x",null,{"station_id":"3","name":"c","lat":35.0,"lon":139.0}]}}"""u8;
        Assert.Single(GbfsParser.ParseStationInformation(info, Operator.Docomo));

        var status = """{"data":{"stations":[1,{"station_id":"3","num_bikes_available":1,"vehicle_types_available":[3,{"vehicle_type_id":"2","count":1}],"vehicle_docks_available":["x",{"vehicle_type_ids":["2"],"count":1}]}]}}"""u8;
        var s = Assert.Single(GbfsParser.ParseStationStatus(status)).Value;
        Assert.Single(s.VehicleTypesAvailable);
        Assert.Single(s.VehicleDocksAvailable);

        var types = """{"data":{"vehicle_types":[1,{"vehicle_type_id":"2"}]}}"""u8;
        Assert.Single(GbfsParser.ParseVehicleTypes(types));
    }

    [Fact]
    public void Docomo_StationStatus()
    {
        var s = GbfsParser.ParseStationStatus(Fixtures.Read("docomo_station_status.json"))["5053"];
        Assert.Equal((4, 2, 1792839276L, true, true, true),
                     (s.NumBikesAvailable, s.NumDocksAvailable, s.LastReported, s.IsInstalled, s.IsRenting, s.IsReturning));
        Assert.Empty(s.VehicleTypesAvailable);
        Assert.Empty(s.VehicleDocksAvailable);
    }

    [Fact]
    public void Hello_StationStatus_ReadsVehicleCounts()
    {
        var s = GbfsParser.ParseStationStatus(Fixtures.Read("hellocycling_station_status.json"))["17"];
        Assert.Equal((2, 6, 1792839002L), (s.NumBikesAvailable, s.NumDocksAvailable, s.LastReported));
        Assert.Equal(new VehicleTypeCount("2", 2), Assert.Single(s.VehicleTypesAvailable));
        var dock = Assert.Single(s.VehicleDocksAvailable);
        Assert.Equal(6, dock.Count);
        Assert.Equal(["2"], dock.VehicleTypeIds);
    }

    [Fact]
    public void VehicleTypes()
    {
        var v = Assert.Single(GbfsParser.ParseVehicleTypes(Fixtures.Read("hellocycling_vehicle_types.json")));
        Assert.Equal(new VehicleType("2", "hellocycling bicycle", "bicycle", "electric_assist", 70000), v);
    }

    [Fact]
    public void SystemInformation()
    {
        var hello = GbfsParser.ParseSystemInformation(Fixtures.Read("hellocycling_system_information.json"));
        Assert.Equal("HELLO CYCLING", hello.ShortName);
        Assert.Equal("OpenStreet Corp.", hello.Operator);
        Assert.Equal("https://play.google.com/store/apps/details?id=jp.hellocycling.hellocycling", hello.AndroidStoreUri);

        var docomo = GbfsParser.ParseSystemInformation(Fixtures.Read("docomo_system_information.json"));
        Assert.Equal("docomo bike share service", docomo.Name);
        Assert.Null(docomo.ShortName);
        Assert.Null(docomo.IosStoreUri);
    }
}
