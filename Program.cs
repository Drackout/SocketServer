using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;

namespace SocketServer
{
    class Program
    {
        private static List<Socket> clients = new List<Socket>();
        private static List<int> playerIds = new List<int>();
        private static bool gameStarted = false;
        private static int currentTurn = 0;
        private static int rounds = 1;
        private static int maxRounds = 5;
        private static int maxPlayers = 2;
        private static int assignedId;
        private static int totalBytesSentThisRound = 0;
        private static int totalBytesReceivedThisRound = 0;

        private static List<Entity> playersPrefabInfo = new List<Entity>()
        {
            new Entity(100, 34),
            new Entity(100, 34, 4)
        };
        
        private static List<Entity> enemyPrefabInfo = new List<Entity>()
        {
            new Entity(100, 20),
            new Entity(100, 20),
        };

        public static void Main(string[] args)
        {
            Socket listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Any, 2100);

            listener.Bind(localEndPoint);
            listener.Listen(maxPlayers); // maxPlayers Works as a client max 
            Console.WriteLine("Server started...");

            while (true)
            {
                if (clients.Count < maxPlayers)
                {
                    // Creates a new socket
                    // Adds new connection to the socket
                    Socket client = listener.Accept();
                    clients.Add(client);
                    assignedId = clients.Count;
                    playerIds.Add(assignedId);
                    // Each client is handled individually
                    // First time using threads so here it goes
                    Thread clientThread = new Thread(() => HandleClient(client, assignedId));
                    clientThread.Start();
                }
            }


            static void HandleClient(Socket client, int playerId)
            {
                Console.WriteLine($"Client {clients.Count} thread started ({client.RemoteEndPoint})");
                Console.WriteLine($"Players: {clients.Count}/{maxPlayers}");


                try
                {
                    SAttachIDtoNewPlayer(client, playerId);
                    Broadcast(action: "updateplayerlist");
                    BroadcastMessage($"Player {playerId} has connected!");
                    byte[] buffer = new byte[4];

                    //while ((bytesReceived = client.Receive(buffer)) > 0 && rounds < maxRounds)
                    while (rounds < maxRounds)
                    {
                        int lenReceived = Receive(buffer, client);
                        if (lenReceived == 0) break;

                        UInt32 commandLen = BitConverter.ToUInt32(buffer, 0);

                        var commandBytes = new byte[commandLen];
                        int bytesReceived = Receive(commandBytes, client);
                        if (bytesReceived == 0) break;


                        if (bytesReceived == commandLen)
                        {
                            string msg = Encoding.UTF8.GetString(commandBytes);
                            //Console.WriteLine($"Received (p{playerId}): {msg}");

                            ////////////////////////
                            try
                            {
                                //get json to.. id, command, extra
                                ClientCommand command = null;

                                try
                                {
                                    command = JsonSerializer.Deserialize<ClientCommand>(msg);
                                }
                                catch (Exception e)
                                {
                                    Console.WriteLine("Invalid json."+e);
                                    continue;
                                }
                                
                                if (command.action == null)
                                {
                                    Console.WriteLine("No action.");
                                    continue;
                                }

                                // check if data was sent correctly
                                Console.WriteLine($"Player {playerId} - {command.action} {command.target}");

                                int CliID = command.playerid;
                                string CliAct = command.action;
                                string CliTrgt = command.target;
                                string[] CliExtra = command.extra;


                                if (playerIds[currentTurn] != playerId)
                                {
                                    SendMessage(client, "Not your turn.");
                                    continue;
                                }

                                switch (command.action)
                                {
                                    case "launch":
                                        SGameStart(client);
                                        continue;

                                    case "move":
                                        SMove(client, playerId, CliTrgt);
                                        continue;

                                    case "attack":
                                        SAttack(CliTrgt, playerId, CliID, client);
                                        continue;

                                    case "endturn":
                                        SEndTurn();
                                        continue;
                                        
                                    default:
                                        SendMessage(client, "Unknown action");
                                        continue;
                                }

                                //.........
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine("Failed to deserialize JSON: " + e.Message);
                            }
                            ////////////////////////


                            //ID:Action:Extra - 1:move:up, 1:launch, 1:attack
                            string[] parts = msg.Split(':');
                            if (parts.Length < 2)
                            {
                                Console.WriteLine($"Invalid message received (< 2)");
                                continue;
                            }


                            if (playerId == -1)
                            {
                                playerId = Convert.ToInt16(parts[0]);
                                if (!playerIds.Contains(playerId))
                                {
                                    playerIds.Add(playerId);
                                }
                            }
                            if (parts[1] == "launch" && playerId == 1 && !gameStarted && clients.Count == maxPlayers)
                            {
                                gameStarted = true;
                                Broadcast($"Game started! It's player{playerIds[currentTurn]} Turn:{playerIds[currentTurn]}");
                                continue;
                            }
                            else if (parts[1] == "launch" && playerId == 1 && !gameStarted && clients.Count != maxPlayers)
                            {
                                Send(client, $"Need more players - {clients.Count}/{maxPlayers}");
                                continue;
                            }

                            // SEND WHEN reached
                            if (!gameStarted && clients.Count == maxPlayers)
                            {
                                BroadcastMessage("Waiting for Host to start the Game.");
                            }

                            if (parts[1] == "launch" && gameStarted)
                            {
                                Send(client, "Game already started");
                                continue;
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error with player {playerId}: {e.Message}");
                }
                Broadcast($"Reached the Max rounds! ({maxRounds})");

                clients.Remove(client);
                playerIds.Remove(playerId);
                client.Shutdown(SocketShutdown.Both);
                client.Close();
            }




            static int Receive(byte[] data, Socket cliSocket, bool accountForLittleEndian = true)
            {
                try
                {
                    int nBytes = cliSocket.Receive(data);
                    totalBytesReceivedThisRound += nBytes;
                    if (accountForLittleEndian && (!BitConverter.IsLittleEndian))
                        Array.Reverse(data);
                    return nBytes;
                }
                catch (SocketException e)
                {
                    if (e.SocketErrorCode == SocketError.WouldBlock)
                    {
                        // Didn't receive any data, just return 0
                        return 0;
                    }
                    else
                    {
                        //Debug.LogError(e);
                    }
                }
                // Return -1 if there's an error
                return -1;
            }

            // Send Message to an Individual Player 
            static void Send(Socket client, string action = "", string message = "", int playerId = 0, string[] extra = null)
            {
                if (extra == null)
                    extra = new string[0];

                SendMessage msg = new SendMessage()
                {
                    playerid = playerId,
                    action = action,
                    message = message,
                    extra = extra
                };

                //Console.WriteLine($"Send_ playerId: {msg.playerid}, action: '{msg.action}', message: '{msg.message}', extra: '{msg.extra}'");

                var setting = new JsonSerializerOptions { PropertyNamingPolicy = null }; //disable Camel Case
                string json = JsonSerializer.Serialize(msg, setting);
                byte[] data = Encoding.UTF8.GetBytes(json);
                byte[] dataLen = BitConverter.GetBytes((UInt32)data.Length);

                Console.WriteLine("Send JSON: " + json);

                try
                {
                    client.Send(dataLen);
                    client.Send(data);
                    totalBytesSentThisRound += data.Length;
                }
                catch (Exception e)
                {
                    Console.WriteLine("Error sending message to client: " + e);
                }
            }

            // Send Message to all connected players
            static void Broadcast(string message = "", string action = "", int playerid = 0, string[] extra = null)
            {
                if (extra == null)
                    extra = new string[0];
                    
                SendMessage msg = new SendMessage()
                {
                    playerid = playerid,
                    action = action,
                    message = message,
                    extra = extra
                };

                //Console.WriteLine($"Broadcast_ playerId: {msg.playerid}, action: '{msg.action}', message: '{msg.message}', extra: '{msg.extra}'");


                var setting = new JsonSerializerOptions { PropertyNamingPolicy = null };
                string json = JsonSerializer.Serialize(msg, setting);
                byte[] data = Encoding.UTF8.GetBytes(json);
                byte[] dataLen = BitConverter.GetBytes((UInt32)data.Length);

                Console.WriteLine("Broadcasting JSON: " + json);

                foreach (Socket client in clients)
                {
                    try
                    {
                        client.Send(dataLen);
                        client.Send(data);
                        totalBytesSentThisRound += data.Length;
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Error sending message to client: " + e);
                    }
                }
            }

            static void BroadcastMessage(string msg)
            {
                Broadcast(message: msg);
            }
            
            static void SendMessage(Socket client, string msg)
            {
                Send(client, message: msg);
            }

            static void SAttachIDtoNewPlayer(Socket client, int newPlayerID)
            {
                Console.WriteLine($"Give ID to player:{newPlayerID}");
                Send(client, action: "getid", playerId: newPlayerID);
            }

            static void SGameStart(Socket client)
            {
                Console.WriteLine($"Start Game");
                if (clients.Count == maxPlayers)
                {
                    gameStarted = true;
                    Broadcast(action: "gamestart", playerid: playerIds[currentTurn]);
                }
                else
                    Send(client, message: $"Need more players - {clients.Count}/{maxPlayers}");
            }

            static void SMove(Socket client, int playerId, string target)
            {
                int IDPrefab = playerId - 1;
                if (gameStarted)
                {
                    int energy = playersPrefabInfo[IDPrefab].getEnergy();
                    int movement = playersPrefabInfo[IDPrefab].getMovement();

                    string[] extraSend;

                    if (energy >= 20)
                    {
                        playersPrefabInfo[IDPrefab].takeEnergy(20);
                        string energyLeft = playersPrefabInfo[IDPrefab].getEnergy().ToString();
                        extraSend = new string[] { movement.ToString(), energyLeft};
                        Broadcast(target, "canmove", playerId, extraSend);
                        BroadcastMessage($"Player{playerId} walked {movement} units {target}, has {energyLeft} energy left!");

                    }
                    else
                    {
                        Send(client, message: $"Not enough Energy! Player energy: {energy}");
                    }
                }
            }
            
            static void SAttack(string target, int playerId, int attacker,Socket client)
            {
                string[] targetInfo = target.ToLower().Split(" ");
                string unit = targetInfo[0];
                int unitID = Convert.ToInt16(targetInfo[1]);
                int attackDamage = playersPrefabInfo[playerId - 1].getDamage();
                int hpRemaining = 0;
                string[] exHp = null;
                string actionSend;
                int energy = playersPrefabInfo[playerId - 1].getEnergy();

                if (energy >= 30)
                {
                    if (unit == "enemy")
                    {
                        enemyPrefabInfo[unitID].loseHP(attackDamage);
                        hpRemaining = enemyPrefabInfo[unitID].getHP();
                        exHp = new string[] { hpRemaining.ToString(), (energy-30).ToString(), attacker.ToString()};

                    }
                    else if (unit == "player")
                    {
                        playersPrefabInfo[unitID - 1].loseHP(attackDamage);
                        hpRemaining = playersPrefabInfo[unitID - 1].getHP();
                        exHp = new string[] { hpRemaining.ToString(), (energy-30).ToString(), attacker.ToString()};
                    }

                    if (hpRemaining > 0)
                        actionSend = "attack";
                    else
                        actionSend = "killed";

                    playersPrefabInfo[playerId - 1].takeEnergy(30);
                    Broadcast(unit, actionSend, unitID, exHp);
                    BroadcastMessage($"Player{playerId} {actionSend} {unit}{unitID}, dealing {attackDamage} damage, {hpRemaining} HP left, has {energy-30} energy left!");
                }
                else
                {
                    Send(client, message: $"Not enough Energy! Player energy: {energy}");
                }
            }

            static void SEndTurn()
            {
                Console.WriteLine($"Round{rounds}, Sent: {totalBytesSentThisRound} bytes, Received: {totalBytesReceivedThisRound} bytes");
    
                BroadcastMessage($"Player{currentTurn + 1} - finished his turn");
                playersPrefabInfo[currentTurn].refilEnergy();

                string currentEnergy = playersPrefabInfo[currentTurn].getEnergy().ToString();

                currentTurn = (currentTurn + 1) % playerIds.Count;

                if (currentTurn == 0)
                {
                    rounds++;
                    BroadcastMessage($"--- Round {rounds} ---");
                }

                int nextPlayerId = playerIds[currentTurn];
                Broadcast(currentEnergy, "nextturn", nextPlayerId);
            }
        }        
    }
}
