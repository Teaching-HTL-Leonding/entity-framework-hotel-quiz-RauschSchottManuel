using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

var factory = new HotelContextFactory();


List<Hotel> hotels = new();

Console.Write("Do you want to insert new hotels? (y/n): ");
var newHotels = Console.ReadLine() is "y";

if(newHotels)
{  
    var userInput = AskForUserInput();

    if (userInput.Count > 0)
    {
        hotels = ProcessUserInput(userInput);

        await AddData(factory);
    }

}

Console.Write("Export to Markdown? (y/n): ");
var exportToMarkdown = Console.ReadLine() is "y";

if(exportToMarkdown)
{
    var hotelsQueried = await QueryData(factory);
    await ExportToMarkdown(hotelsQueried);
    Console.WriteLine("Finished exporting Markdown to file");

}


async Task AddData(HotelContextFactory factory)
{
    var context = factory.CreateDbContext();

    await context.Hotels.AddRangeAsync(hotels);

    await context.SaveChangesAsync();
}

#region Querying & Export Logic
async Task<List<Hotel>> QueryData(HotelContextFactory factory)
{
    var context = factory.CreateDbContext();

    List<Hotel> hotels = await context.Hotels
        .Include(h => h.Specials)
        .Include(h => h.RoomTypes)
        .ToListAsync();

    foreach (var hotel in hotels)
    {
        foreach(var roomType in hotel.RoomTypes)
        {
           await context.Entry(roomType).Collection(i => i.RoomPrices).LoadAsync();
        }
    }

    return hotels;
}

async Task ExportToMarkdown(List<Hotel> hotels)
{
    Console.WriteLine("Generating content ...");
    var mdContentBuilder = new StringBuilder();
    foreach (var hotel in hotels)
    {
        mdContentBuilder.Append($"# {hotel.Name}");
        mdContentBuilder.AppendLine().AppendLine();

        mdContentBuilder.Append($"## Location");
        mdContentBuilder.AppendLine().AppendLine();
        mdContentBuilder.Append($"{hotel.Street}").AppendLine();
        mdContentBuilder.Append($"{hotel.ZipCode} {hotel.City}");
        mdContentBuilder.AppendLine().AppendLine();

        mdContentBuilder.Append("## Specials");
        mdContentBuilder.AppendLine().AppendLine();
        foreach(var special in hotel.Specials)
        {
            var specialType = special.Type.GetType().GetMember(special.Type.ToString()).First();
            var specialDisplayName = specialType.GetCustomAttribute<DisplayAttribute>();

            mdContentBuilder.Append($"* {specialDisplayName.Name}").AppendLine();
        }

        mdContentBuilder.AppendLine().Append("## Room Types").AppendLine().AppendLine();
        mdContentBuilder.Append("| Room Name | Description | Size | Disability-Accessable | Available room count | Price Valid From | Price Valid Till | Price in € |").AppendLine();
        mdContentBuilder.Append("| --------- | ----------- | ---: | --------------------- | --------- | ---------------- | ---------------- | ---------: |").AppendLine();

        DateTime? fromDate, toDate;

        foreach(var roomType in hotel.RoomTypes)
        {
            fromDate = roomType.RoomPrices.Find(p => p.RoomTypeId.Equals(roomType.Id)).From;
            toDate = roomType.RoomPrices.Find(p => p.RoomTypeId.Equals(roomType.Id)).To;
            mdContentBuilder.Append(@$"| {roomType.Title} | {roomType.Description} | {roomType.Size} $m^2$ | {(roomType.DisabilityFriendly is true ? "Yes" : "No")} | {roomType.RoomsAvailable} | {(fromDate is not null ? ((DateTime)fromDate).ToString("dd.MM.yyyy") : string.Empty)} | {(toDate is not null ? ((DateTime)toDate).ToString("dd.MM.yyyy") : string.Empty)} | {roomType.RoomPrices.Find(p => p.RoomTypeId.Equals(roomType.Id)).PricePerNight} €|").AppendLine();
        }

        Console.WriteLine("Hotel finished");
    }

    var markdownFileContent = mdContentBuilder.ToString();
    Console.WriteLine("Content generated!");

    var filePath = AskForFilePath();

   bool successful = await WriteMarkdownToFile(filePath, markdownFileContent);
}

string AskForFilePath()
{
    Console.WriteLine("Please input a valid path to a file where the markdown should be exported to: ");
    return Console.ReadLine();
}

async Task<bool> WriteMarkdownToFile(string filePath, string content)
{
    try
    {
        await File.WriteAllTextAsync(filePath, content);
        return true;
    }
    catch (Exception e)
    {
        Console.WriteLine(e.StackTrace);
        return false;
    }
}
#endregion

#region UserInput Logic
List<(string hotelName, string hotelAdress, string specials, List<string> rooms, List<string> roomPrices)> AskForUserInput()
{
    List<(string hotelName, string hotelAdress, string specials, List<string> rooms, List<string> roomPrices)> userInput = new();
    string hotelName, hotelAdress, specials, room, roomPricing;
    int numberOfRooms;
    List<string> rooms = new(), roomPricings = new();

    Console.Write("Wie viele Hotels wollen Sie eingeben? => ");
    var numberOfHotels = int.Parse(Console.ReadLine());

    for(int i = 0; i < numberOfHotels; i++)
    {
        Console.WriteLine("\t\tNeues Hotel");

        Console.Write("Hotel Name: ");
        hotelName = Console.ReadLine();

        Console.Write("Adresse (Format: <Straße + Hausnummer>,<PLZ>,<Ort>): ");
        hotelAdress = Console.ReadLine();

        Console.Write("Specials (mit ',' getrennt in CamelCase angeben): ");
        specials = Console.ReadLine();

        Console.Write("Wie viele verschiedene Räume wollen Sie eingeben? => ");
        numberOfRooms = int.Parse(Console.ReadLine());

        for(int k = 0; k < numberOfRooms; k++)
        {
            Console.Write("Raum (<Name>,<Beschreibung>,<Größe>,<Behindertengerecht(ja/nein)>,<Stückanzahl>): ");
            room = Console.ReadLine();

            Console.Write("<VON WANN>, <BIS WANN>, ist der Raum wie <TEUER>? (mit ',' abtrennen): ");
            roomPricing = Console.ReadLine();

            rooms.Add(room);
            roomPricings.Add(roomPricing);
        }

        userInput.Add((hotelName, hotelAdress, specials, rooms, roomPricings));
    }

    return userInput;
}

List<Hotel> ProcessUserInput(List<(string hotelName, string hotelAdress, string specials, List<string> rooms, List<string> roomPrices)> userInput)
{
    List<Hotel> hotels = new();
    string[] splittedAddress, splittedSpecials, splittedRoom, splittedPrices;

    foreach(var item in userInput)
    {
        splittedAddress = item.hotelAdress.Split(",");
        splittedSpecials = item.specials.Split(",");
        List<HotelSpecial> specials = new();
        foreach(var special in splittedSpecials)
        {
            specials.Add(new HotelSpecial() { Type = Enum.Parse<EHotelSpecial>(special) });
        }
        List<RoomType> roomTypes = new();

        for (int j = 0; j < item.rooms.Count; j++)
        {
            splittedRoom = item.rooms[j].Split(",");
            splittedPrices = item.roomPrices[j].Split(",");
            roomTypes.Add(new()
            {
                Title = splittedRoom[0],
                Description = splittedRoom[1],
                Size = splittedRoom[2],
                DisabilityFriendly = splittedRoom[3] is "ja",
                RoomsAvailable = int.Parse(splittedRoom[4]),
                RoomPrices = new()
                {
                    new RoomPrice()
                    {
                        From = DateTime.Parse(splittedPrices[0]),
                        To = DateTime.Parse(splittedPrices[1]),
                        PricePerNight = int.Parse(splittedPrices[2]),
                    },
                }
            });
        }

        hotels.Add(new()
        {
            Name = item.hotelName,
            Street = splittedAddress[0],
            ZipCode = int.Parse(splittedAddress[1]),
            City = splittedAddress[2],
            Specials = specials,
            RoomTypes = roomTypes,
        });
    }

    return hotels;
}
#endregion

#region Model
class Hotel
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Street { get; set; }
    public int ZipCode { get; set; }
    public string City { get; set; }

    public List<HotelSpecial> Specials { get; set; }

    public List<RoomType> RoomTypes { get; set; }
}

enum EHotelSpecial
{
    [Display(Name = "Spa")]
    Spa,
    [Display(Name = "Sauna")]
    Sauna,
    [Display(Name = "Dog friendly")]
    DogFriendly,
    [Display(Name = "Indoor pool")]
    IndoorPool,
    [Display(Name = "Outdoor pool")]
    OutdoorPool,
    [Display(Name = "Bike rental")]
    BikeRental,
    [Display(Name = "e-Car charging station")]
    ECarChargingStation,
    [Display(Name = "Vegetarian cuisine")]
    VegetarianCuisine,
    [Display(Name = "Organic food")]
    OrganicFood
}

class HotelSpecial
{
    public int Id { get; set; }

    public EHotelSpecial Type { get; set; }

    public int HotelId { get; set; }
    public Hotel Hotel { get; set; }
}

class RoomType
{
    public int Id { get; set; }

    public int HotelId { get; set; }
    public Hotel Hotel { get; set; }

    [MaxLength(300)]
    public string Title { get; set; }

    public string Description { get; set; }

    public string Size { get; set; }

    public bool DisabilityFriendly { get; set; }

    public int RoomsAvailable { get; set; }

    public List<RoomPrice> RoomPrices { get; set; }
}

class RoomPrice
{
    public int Id { get; set; }

    public int RoomTypeId { get; set; }
    public RoomType RoomType { get; set; }

    private DateTime? _from;
    public DateTime? From
    {
        get => _from;
        set
        {
            if (value.Value.CompareTo(DateTime.Now) > 0)
                _from = value;
        }
    }

    private DateTime? _to;
    public DateTime? To 
    {
        get => _to;
        set
        {
            if (From is not null && value.Value.CompareTo(From) > 0)
                _to = value;
            else if (From is null && value.Value.CompareTo(DateTime.Now) > 0)
                _to = value;
        }
    }
    public int PricePerNight { get; set; }
}
#endregion

#region DBScheme
class HotelContext : DbContext
{
    public HotelContext(DbContextOptions<HotelContext> options) :base(options) { }

    public DbSet<Hotel> Hotels { get; set; }

    public DbSet<HotelSpecial> HotelSpecials { get; set; }

    public DbSet<RoomType> RoomTypes { get; set; }

    public DbSet<RoomPrice> RoomPrices { get; set; }
}

class HotelContextFactory : IDesignTimeDbContextFactory<HotelContext>
{
    public HotelContext CreateDbContext(string[] args = null)
    {
        var configuration = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build();

        var optionsBuilder = new DbContextOptionsBuilder<HotelContext>();
        optionsBuilder.UseSqlServer(configuration["ConnectionStrings:DefaultConnection"]);

        return new HotelContext(optionsBuilder.Options);
    }
}
#endregion