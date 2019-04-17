using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Net;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Unosquare.Labs.EmbedIO;
using Unosquare.Labs.EmbedIO.Modules;
using Unosquare.Labs.EmbedIO.Constants;


namespace Fatumbot {
    public static class Globals {
        public static IFormatProvider invariant = System.Globalization.CultureInfo.InvariantCulture;
    }

    public struct Coords {
        public double lat, lng;
        public Coords(double latitude, double longitude) {
            lat = latitude;
            lng = longitude;
        }
    }
    public enum FatumLocationType {
        PsuedoRandom,
        QuantumRandom,
        Attractor,
        Repeller,
    }
    public struct FatumLocation {
        public Coords position;
        public double strength;
        public double distance;
        public double radius;
        public FatumLocationType type;

        public FatumLocation(double pLatitude, double pLongitude, FatumLocationType pType, double pDistance = 0, double pStrength = 0, double pRadius = 0) {
            position = new Coords(pLatitude, pLongitude);
            type = pType;
            strength = pStrength;
            distance = pDistance;
            radius = pRadius;
        }
    }
    public class returnMessage {
        // In
        public string taskName { get; set; }
        public Coords searchOrigin { get; set; }
        public int searchRadius { get; set; }
        // Out
        public string message { get; set; }
        public List<FatumLocation> generatedLocations { get; set; }
        public bool success { get; set; }
        public int processTime { get; set; }
        public returnMessage(string iTaskName, List<FatumLocation> iGeneratedLocations, string iMessage = "", Coords iSearchOrigin = new Coords(), int iSearchRadius = -1) {
            message = iMessage;
            searchOrigin = iSearchOrigin;
            searchRadius = iSearchRadius;

            generatedLocations = iGeneratedLocations;
            taskName = iTaskName;

            success = true;
            processTime = 0;
        }
    }

    class taskController : Program {
        public static string httpLogFile = "httpLog.txt";
        static private void logTask(string taskName, int userId, int[] timestamp, returnMessage returnMsg) {
            string outMap = "";
            if (returnMsg.generatedLocations.Count > 0) { 
                foreach (FatumLocation flocation in returnMsg.generatedLocations) {
                    outMap += @"http://wikimapia.org/#lang=en&lat=";
                    outMap += flocation.position.lat.ToString("#0.000000", Globals.invariant) + "&lon=" 
                        + flocation.position.lng.ToString("#0.000000", Globals.invariant) + "&z=19&m=b";
                    outMap += Environment.NewLine;
                }
            }
            string shortMessage = returnMsg.message.Replace(System.Environment.NewLine, "|");
            try {
                System.IO.File.AppendAllText(httpLogFile,
                    "StartTime (" + timestamp[0].ToString() + ") " +
                    "EndTime (" + timestamp[1].ToString() + ") " +
                    "TotalTime (" + returnMsg.processTime.ToString() + " sec)" +
                    " | User:" + userId.ToString() + Environment.NewLine +
                    " - Requested task '" + taskName + "' with output: " + shortMessage + " " + outMap + Environment.NewLine + Environment.NewLine);
            } catch (Exception e) { Console.WriteLine("Error writing to log file" + Environment.NewLine + e.Message.ToString()); }
        }

        static public returnMessage http_DoWorkTask(string taskName, int userId, int startTime, Coords searchOrigin, int searchRadius) {
            Console.WriteLine("Doing Task: " + taskName);

            List<FatumLocation> generatedLocations = new List<FatumLocation>();
            returnMessage returnMsg = new returnMessage(taskName, generatedLocations);

            if (banned.ContainsKey(userId)) {
                returnMsg.message = "Error";
                return returnMsg;
            }

            // validate inputs - this is done before any transfers to user session as session data .should. already have valid values
            bool validInput = true;
            if (searchOrigin.lat < -90 || searchOrigin.lat > 90) {
                validInput = false;
                returnMsg.message = "Latitude must be greater than -90 and less than 90.";
            }
            if (searchOrigin.lng < -180 || searchOrigin.lng > 180) {
                validInput = false;
                returnMsg.message = "Longitude must be greater than -180 and less than 180.";
            }
            if (searchRadius < 1000 && searchRadius != -1) { //-1 will update from user session
                validInput = false;
                returnMsg.message = "Minimum radius is 1000 m.";
            }
            if (searchRadius > 1000000) {
                validInput = false;
                returnMsg.message = "Maximum search radius is 1000000 m";
            }
            if (!validInput) {
                returnMsg.success = false;
                return returnMsg;
            }

            // Create a user session if one doesnt exist
            if (usessions.ContainsKey(userId) == false) {   
                int u = usessions.Count;
                usessions.Add(userId, u);
                SetDefault(u);
            }
            // Set up user session from api input or vice versa
            double appi = upresets[(int)usessions[userId], 4];
            if (searchOrigin.Equals(default(Coords))) {
                searchOrigin = new Coords(upresets[(int)usessions[userId], 1], upresets[(int)usessions[userId], 2]);
            } else {
                upresets[(int)usessions[userId], 1] = searchOrigin.lat;
                upresets[(int)usessions[userId], 2] = searchOrigin.lng;
            }
            if (searchRadius <= 0) {
                searchRadius = (int)upresets[(int)usessions[userId], 0];
            } else {
                upresets[(int)usessions[userId], 0] = searchRadius;
            }
            upresets[(int)usessions[userId], 4] = (searchRadius * appikm) / 1000;
            if (upresets[(int)usessions[userId], 4] < minappi) { upresets[(int)usessions[userId], 4] = minappi; }
            upresets[(int)usessions[userId], 3] = 0;
            
            // After validation and population from user session, set up output
            returnMsg.searchOrigin = searchOrigin;
            returnMsg.searchRadius = searchRadius;

            // Then perform requested task
            double[] incoords = new double[10];
            if ((taskName == "getpseudo")) {
                try {
                    incoords = GetPseudoRandom(searchOrigin.lat, searchOrigin.lng, searchRadius);
                    int distance = GetDistance(searchOrigin.lat, searchOrigin.lng, incoords[0], incoords[1]);
                    generatedLocations.Add(new FatumLocation(incoords[0], incoords[1], FatumLocationType.PsuedoRandom, distance));
                    returnMsg.message = "Psuedorandom point generated";
                } catch (Exception e) { Console.WriteLine("getpseudo Command processing error" + Environment.NewLine + e.Message.ToString()); }
            } else if (taskName == "getquantum") {
                try {
                    incoords = GetQuantumRandom(searchOrigin.lat, searchOrigin.lng, searchRadius);
                    int distance = GetDistance(searchOrigin.lat, searchOrigin.lng, incoords[0], incoords[1]);
                    generatedLocations.Add(new FatumLocation(incoords[0], incoords[1], FatumLocationType.QuantumRandom, distance));
                    returnMsg.message = "QuantumRandom point generated";
                } catch (Exception e) { Console.WriteLine("getquantum command processing error " + Environment.NewLine + e.Message.ToString()); }
            } else if (taskName == "getattractor") {
                try {
                    string mesg;
                    incoords = GetQuantumAttractor(searchOrigin.lat, searchOrigin.lng, searchRadius, appi);

                    if (incoords[2] < 1.3) { mesg = "Attractor is invalid! power: " + incoords[2].ToString("#0.00", Globals.invariant); }
                    else if (incoords[2] >= 2) { mesg = "Attractor generated. power: " + incoords[2].ToString("#0.00", Globals.invariant); }
                    else { mesg = "Attractor generated. power: " + incoords[2].ToString("#0.00", Globals.invariant) + " (Weak)"; }

                    if (String.IsNullOrEmpty(mesg)) {
                        returnMsg.success = false;
                        returnMsg.message = "Error generating Quantum Attractor";
                    } else {
                        int distance = GetDistance(searchOrigin.lat, searchOrigin.lng, incoords[0], incoords[1]);
                        generatedLocations.Add(new FatumLocation(incoords[0], incoords[1], FatumLocationType.Attractor, distance, incoords[2]));
                        returnMsg.message += mesg;
                    }
                } catch (Exception e) { Console.WriteLine("getattractor command processing error" + Environment.NewLine + e.Message.ToString()); }
            } else if (taskName == "getrepeller" || taskName == "getvoid") {
                try {
                    string mesg;
                    incoords = GetQuantumRepeller(searchOrigin.lat, searchOrigin.lng, searchRadius, appi);
                    if (incoords[2] >= 0.9) {
                        mesg = "Void Attractor is invalid! power: " 
                                + (1 / incoords[2]).ToString("#0.00", Globals.invariant)
                                + " radius: " + incoords[3].ToString("#0.00", Globals.invariant) + " meters";
                    } else if (incoords[2] < 0.6) {
                        mesg = "Void Attractor generated. power: " 
                                + (1 / incoords[2]).ToString("#0.00", Globals.invariant)
                                + " radius: " + incoords[3].ToString("#0.00", Globals.invariant) + " meters";
                    } else {
                        mesg = "Void Attractor generated. power: " 
                                + (1 / incoords[2]).ToString("#0.00", Globals.invariant) + " (Weak) "
                                 + " radius: " + incoords[3].ToString("#0.00", Globals.invariant) + " meters";
                    }

                    if (String.IsNullOrEmpty(mesg)) {
                        returnMsg.success = false;
                        returnMsg.message = "Error generating Quantum Repeller";
                    } else {
                        int distance = GetDistance(searchOrigin.lat, searchOrigin.lng, incoords[0], incoords[1]);
                        generatedLocations.Add(new FatumLocation(incoords[0], incoords[1], FatumLocationType.Repeller, distance, (1 / incoords[2]), incoords[3]));
                        returnMsg.message += mesg;
                    }
                } catch (Exception e) { Console.WriteLine("getvoid command processing error" + Environment.NewLine + e.Message.ToString()); }
            } else if (taskName == "getpair") {
                try {
                    Console.WriteLine("GetQuantumPair BEGIN");
                    incoords = GetQuantumPair(searchOrigin.lat, searchOrigin.lng, searchRadius, appi);
                    Console.WriteLine("GetQuantumPair COMPLETE");
                    string mesg1;
                    if (incoords[4] < 1.3) { mesg1 = "Attractor is invalid! power: " + incoords[4].ToString("#0.00", Globals.invariant); }
                    else if (incoords[4] >= 2) { mesg1 = "Attractor generated. power: " + incoords[4].ToString("#0.00", Globals.invariant); }
                    else { mesg1 = "Attractor generated. power: " + incoords[4].ToString("#0.00", Globals.invariant) + " (Weak)"; }

                    if (String.IsNullOrEmpty(mesg1)) {
                        returnMsg.success = false;
                        returnMsg.message = "Error generating Quantum Attractor for Pair";
                    } else {
                        int distance = GetDistance(searchOrigin.lat, searchOrigin.lng, incoords[0], incoords[1]);
                        generatedLocations.Add(new FatumLocation(incoords[0], incoords[1], FatumLocationType.Attractor, distance, incoords[4]));
                        returnMsg.message += mesg1;
                    }

                    string mesg2 ;
                    if (incoords[5] >= 0.9) {
                        mesg2 = " Void Attractor is invalid! power: " 
                                + (1 / incoords[5]).ToString("#0.00", Globals.invariant)
                                + " radius: " + incoords[6].ToString("#0.00", Globals.invariant) + " meters";
                    } else if (incoords[5] < 0.6) {
                        mesg2 = " Void Attractor generated. power: " 
                                + (1 / incoords[5]).ToString("#0.00", Globals.invariant)
                                + " radius: " + incoords[6].ToString("#0.00", Globals.invariant) + " meters";
                    } else {
                        mesg2 = " Void Attractor generated. power: " + (1 / incoords[5]).ToString("#0.00", Globals.invariant) + " (Weak) "
                                + " radius: " + incoords[6].ToString("#0.00", Globals.invariant) + " meters";
                    }

                    if (String.IsNullOrEmpty(mesg2)) {
                        returnMsg.success = false;
                        returnMsg.message = "Error generating Quantum Repeller for Pair";
                    } else {
                        int distance = GetDistance(searchOrigin.lat, searchOrigin.lng, incoords[2], incoords[3]);
                        generatedLocations.Add(new FatumLocation(incoords[2], incoords[3], FatumLocationType.Repeller, distance, incoords[5], incoords[6]));
                        returnMsg.message += mesg2;
                    }
                } catch (Exception e) { Console.WriteLine("getpair command processing error" + Environment.NewLine + e.Message.ToString()); }
            } else if (taskName == "setdefault") {
                try {
                    if (usessions.ContainsKey(userId) == false) {
                        int u = usessions.Count;
                        usessions.Add(userId, u);
                        SetDefault(u);
                    } else {
                        SetDefault((int)usessions[userId]);
                    }
                    returnMsg.searchOrigin = searchOrigin;
                    returnMsg.searchRadius = searchRadius;
                    returnMsg.message = "Reset completed";
                } catch (Exception e) { Console.WriteLine("setdefault command processing error" + Environment.NewLine + e.Message.ToString()); }
            } else if (taskName == "test" || taskName == "status") {
                 returnMsg.message = "Fatum-2 is online";
            } else if (taskName == "help" || taskName == "start") {
                try {
                    string alltxt = System.IO.File.ReadAllText("help.txt", System.Text.Encoding.GetEncoding(1251));
                    returnMsg.message = alltxt;
                } catch (Exception e) { Console.WriteLine("Heplfile error " + Environment.NewLine + e.Message.ToString()); }
            } else if (taskName == "setlocation") {
                // this is done at the start of the function block, just need to send a response to user here
                returnMsg.message = "location has been updated from API input or saved user session, otherwise set to default values";
            } else if (taskName == "setradius") {
                // this is done at the start of the function block, just need to send a response to user here
                returnMsg.message = "Radius has been updated from API input or saved user sesion, otherwise set to default values";
            } else {
                returnMsg.success = false;
                returnMsg.message = "Unkown task";
            }

            int endTime = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            int elapsedTime = endTime - startTime;
            returnMsg.processTime = elapsedTime;

            logTask(taskName, userId, new int[] { startTime, endTime, elapsedTime }, returnMsg);
            return returnMsg;
    }

    }
    public class RESTController : WebApiController {
        public static IHttpContext sContext;
        public RESTController(IHttpContext context) : base(context) {
            sContext = context;
        }

        [WebApiHandler(HttpVerbs.Get, new string[] { "/api/tasks/{taskName}/", "/api/tasks/{taskName}/{origin}/{radius}/", "/api/tasks/{taskName}/{originRadius}" })]
        public async Task<bool> doTaskByName(string taskName, string origin, string radius, string originRadius) {
            IPAddress userIP = sContext.Request.RemoteEndPoint.Address;
            int userId = userIP.GetHashCode();
            int unixTimestamp = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            int searchRadius;
            Coords searchOrigin = new Coords();

            // TO DO this is the wrong token, need to get the token from the specific handler request
            // - not sure if this is possible with WebApiController may need to rewrite to an extension of WebModuleBase and use the ct from the WebHandler delegate on WebModuleBase.AddModule
            // Used to cancel the background processing when the user cancels the http request
            CancellationToken ct = this.WebServer.Module<WebApiModule>().CancellationToken;

            // Handle ambiguous inputs
            if (String.IsNullOrEmpty(radius) && String.IsNullOrEmpty(origin)) {
                if (!String.IsNullOrEmpty(originRadius)) {
                    if (originRadius.Contains(",")) {
                        origin = originRadius;
                        searchRadius = -1;
                    } else {
                        origin = "";
                        radius = originRadius;
                    }
                } else {
                    origin = "";
                    searchRadius = -1;
                }
            }

            // Sanitize/format inputs
            string[] latlong = origin.Split(',');
            double lat, lng;
            if (double.TryParse(latlong[0], out lat) && double.TryParse(latlong[1], out lng))
                searchOrigin = new Coords(lat, lng);
            if (!Int32.TryParse(radius, out searchRadius))
                searchRadius = -1;       //http_DoWorkTask will perform validation

            // Run task
            try {
                returnMessage item = await Task.Run(() => taskController.http_DoWorkTask(taskName, userId, unixTimestamp, searchOrigin, searchRadius), ct);
                return await this.JsonResponseAsync(item);
            } catch (Exception ex) {
                return await this.JsonExceptionResponseAsync(ex);
            }
        }

        public override void SetDefaultHeaders() => this.NoCache();
    }
}
