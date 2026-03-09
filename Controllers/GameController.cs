using Microsoft.AspNetCore.Mvc;

namespace webapp_miniproject.Controllers
{
    public class GameController : Controller
    {
        [HttpGet]
        public JsonResult GetGames()
        {
            // ทดสอบ ข้อมูลเกมแบบคงที่ 
            var games = new[]
            {
                new { id = 1, name = "Garena: RoV", image = "https://s.isanook.com/ga/0/ud/214/1072921/rov-1.jpg?ip/crop/w670h402/q80/jpg" },
                new { id = 2, name = "PUBG Mobile", image = "https://static0.xdaimages.com/wordpress/wp-content/uploads/2018/06/pubg.jpg?w=1200&h=675&fit=crop" },
                new { id = 3, name = "Free Fire", image = "https://encrypted-tbn0.gstatic.com/images?q=tbn:ANd9GcRSL0Uus_M4WwSN8N6NpLQcnJ-ONnHNMjYSyL4zBOxCxtlM80UcbULuocF1NbVKWGqXhx9b-D_HJzefV-yfH9-wYfsDsRj8GQIkfv8SZw&s=10" },
                new { id = 4, name = "Call of Duty: Mobile", image = "https://codm.garena.com/static/images/Main-page/P1/main-kv.jpg" },
                new { id = 5, name = "Valorant", image = "https://www.riotgames.com/darkroom/1440/8d5c497da1c2eeec8cffa99b01abc64b:5329ca773963a5b739e98e715957ab39/ps-f2p-val-console-launch-16x9.jpg" },
                new { id = 6, name = "League of Legends", image = "https://cdn1.epicgames.com/offer/24b9b5e323bc40eea252a10cdd3b2f10/EGS_LeagueofLegends_RiotGames_S1_2560x1440-47eb328eac5ddd63ebd096ded7d0d5ab" },
                new { id = 7, name = "Dota 2", image = "https://cdn.cloudflare.steamstatic.com/steam/apps/570/header.jpg" },
                new { id = 8, name = "Apex Legends", image = "https://cdn.cloudflare.steamstatic.com/steam/apps/1172470/header.jpg" },
                new { id = 9, name = "Overwatch 2", image = "https://cdn.oneesports.gg/cdn-data/2022/10/Overwatch2_KeyArt.jpg" },
                new { id = 10, name = "Fortnite", image = "https://cdn2.unrealengine.com/fortnite-chapter4-season1-keyart-3840x2160.jpg" },
                new { id = 11, name = "Genshin Impact", image = "https://upload-os-bbs.hoyolab.com/upload/2021/09/28/1015537/7d0d2c9c3b6e63c8799a39af51ed245d_6113573153468966613.jpg" },
                new { id = 12, name = "Minecraft", image = "https://cdn.cloudflare.steamstatic.com/steam/apps/1672970/header.jpg" },
                new { id = 13, name = "Counter-Strike 2", image = "https://cdn.cloudflare.steamstatic.com/steam/apps/730/header.jpg" },
                new { id = 14, name = "Rainbow Six Siege", image = "https://cdn.cloudflare.steamstatic.com/steam/apps/359550/header.jpg" },
                new { id = 15, name = "Mobile Legends", image = "https://cdn.oneesports.gg/cdn-data/2022/02/MobileLegends_KeyArt.jpg" }
            };

            return Json(games);
        }
    }
}