namespace SocketServer
{
    public class ClientCommand
    {
        public int playerid { get; set; }
        public string action { get; set; }
        public string target { get; set; }
        public string[] extra { get; set; }
    }
}