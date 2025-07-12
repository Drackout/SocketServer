namespace SocketServer
{
    public class SendMessage
    {
        public int playerid { get; set; }
        public string action { get; set; }
        public string message { get; set; }
        public string[] extra { get; set; }
    }
}