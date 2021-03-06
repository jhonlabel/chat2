using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Sockets;
using Proshot.CommandServer;
using System.ComponentModel;

namespace ConsoleServer
{
    class Program
    {
        private List<ClientManager> clients; // usa System.Collections.Generic
        private BackgroundWorker bwListener;
        private Socket listenerSocket;
        private IPAddress serverIP;
        private int serverPort;

        static void Main(string [] args) // acepta argumentos
        {
            Program progDomain = new Program(); // ejecuta la clase
            progDomain.clients = new List<ClientManager>(); // crea una lista para los clientes

            if ( args.Length == 0 )
            {
                progDomain.serverPort = 8000;
                progDomain.serverIP = IPAddress.Any; // Utiliza todas las IPs de las interfaces de red
            }
            if ( args.Length == 1) // si se introduce un argumento, utiliza la IP introducida
            {
                progDomain.serverIP = IPAddress.Parse(args [0]);
                progDomain.serverPort = 8000;
            }
            if ( args.Length == 2 ) // si se introduce dos argumentos, utiliza la IP y el puerto
            {
                progDomain.serverIP = IPAddress.Parse(args [0]);
                progDomain.serverPort = int.Parse(args [1]);
            }

            // Por hacer: arreglar por si se introducen 3 argumentos, no falle

            progDomain.bwListener = new BackgroundWorker(); // ejecuta la clase BackgroundWorker. Permite ejecutar m�todos en segundo plano
            progDomain.bwListener.WorkerSupportsCancellation = true; // se establece a true para que BackgroundWorker acepte CancelAsync -> para el m�todo que est� ejecutado en segundo plano
            progDomain.bwListener.RunWorkerAsync(); // env�a petici�n para poder ejecutar el m�todo en segundo plano
            progDomain.bwListener.DoWork += new DoWorkEventHandler(progDomain.StartToListen); // una vez que la petici�n se he enviado, esto ejecuta StartToListen en segundo plano


            Console.WriteLine("*** Listening on port {0}{1}{2} started.Press ENTER to shutdown server. ***\n",progDomain.serverIP.ToString(),":",progDomain.serverPort.ToString());
            
            Console.ReadLine(); // en pausa; si se presiona enter, la aplicaci�n contin�a

            progDomain.DisconnectServer();
        }

        private void StartToListen(object sender , DoWorkEventArgs e) // controlar evento DoWork
        {
            this.listenerSocket = new Socket(AddressFamily.InterNetwork , SocketType.Stream , ProtocolType.Tcp); // crea un socket para enviar datos a trav�s de TCP
            this.listenerSocket.Bind(new IPEndPoint(this.serverIP , this.serverPort)); // asocia el socket con la IP : puerto
            this.listenerSocket.Listen(200); // pone el socket en modo escucha
            while ( true ) // mientras no haya sucedido ningun error / exception ???
                this.CreateNewClientManager(this.listenerSocket.Accept()); // crea la conexi�n al socket
        }
        private void CreateNewClientManager(Socket socket) // manejar a los clientes
        {
            ClientManager newClientManager = new ClientManager(socket);
            newClientManager.CommandReceived += new CommandReceivedEventHandler(CommandReceived); // eje
            newClientManager.Disconnected += new DisconnectedEventHandler(ClientDisconnected); // disconecta al cliente
            this.CheckForAbnormalDC(newClientManager);
            this.clients.Add(newClientManager);
            this.UpdateConsole("Connected." , newClientManager.IP , newClientManager.Port);
        }

        private void CheckForAbnormalDC(ClientManager mngr)
        {
            if ( this.RemoveClientManager(mngr.IP) )
                this.UpdateConsole("Disconnected." , mngr.IP , mngr.Port);
        }

        void ClientDisconnected(object sender , ClientEventArgs e)
        {
            if ( this.RemoveClientManager(e.IP) )
                this.UpdateConsole("Disconnected." , e.IP , e.Port);
        }

        private bool RemoveClientManager(IPAddress ip)
        {
            lock ( this )
            {
                int index = this.IndexOfClient(ip);
                if ( index != -1 )
                {
                    string name = this.clients [index].ClientName;
                    this.clients.RemoveAt(index);

                    //Inform all clients that a client had been disconnected.
                    Command cmd = new Command(CommandType.ClientLogOffInform , IPAddress.Broadcast);
                    cmd.SenderName = name;
                    cmd.SenderIP = ip;
                    this.BroadCastCommand(cmd);
                    return true;
                }
                return false;
            }
        }

        private int IndexOfClient(IPAddress ip)
        {
            int index = -1;
            foreach ( ClientManager cMngr in this.clients )
            {
                index++;
                if ( cMngr.IP.Equals(ip) )
                    return index;
            }
            return -1;
        }

        private void CommandReceived(object sender , CommandEventArgs e)
        {
            //When a client connects to the server sends a 'ClientLoginInform' command with a MetaData in this format :
            //"RemoteClientIP:RemoteClientName". With this information we should set the name of ClientManager and then
            //Send the command to all other remote clients.
            if ( e.Command.CommandType == CommandType.ClientLoginInform )
                this.SetManagerName(e.Command.SenderIP , e.Command.MetaData);
           
            //If the client asked for existance of a name,answer to it's question.
            if ( e.Command.CommandType == CommandType.IsNameExists )
            {
                bool isExixsts = this.IsNameExists(e.Command.SenderIP , e.Command.MetaData);
                this.SendExistanceCommand(e.Command.SenderIP , isExixsts);
                return;
            }
            //If the client asked for list of a logged in clients,replay to it's request.
            else if ( e.Command.CommandType == CommandType.SendClientList )
            {
                this.SendClientListToNewClient(e.Command.SenderIP);
                return;
            }

            if ( e.Command.Target.Equals(IPAddress.Broadcast) )
                this.BroadCastCommand(e.Command);
            else
                this.SendCommandToTarget(e.Command);

        }

        private void SendExistanceCommand(IPAddress targetIP , bool isExists)
        {
            Command cmdExistance = new Command(CommandType.IsNameExists , targetIP , isExists.ToString());
            cmdExistance.SenderIP = this.serverIP;
            cmdExistance.SenderName = "Server";
            this.SendCommandToTarget(cmdExistance);
        }

        private void SendClientListToNewClient(IPAddress newClientIP)
        {
            foreach ( ClientManager mngr in this.clients )
            {
                if ( mngr.Connected && !mngr.IP.Equals(newClientIP) )
                {
                    Command cmd = new Command(CommandType.SendClientList , newClientIP);
                    cmd.MetaData = mngr.IP.ToString() + ":" + mngr.ClientName;
                    cmd.SenderIP = this.serverIP;
                    cmd.SenderName = "Server";
                    this.SendCommandToTarget(cmd);
                }
            }
        }

        private string SetManagerName(IPAddress remoteClientIP , string nameString)
        {
            int index = this.IndexOfClient(remoteClientIP);
            if ( index != -1 )
            {
                string name = nameString.Split(new char [] { ':' }) [1];
                this.clients [index].ClientName = name;
                return name;
            }
            return "";
        }
        private bool IsNameExists(IPAddress remoteClientIP , string nameToFind)
        {
            foreach ( ClientManager mngr in this.clients )
                if ( mngr.ClientName == nameToFind && !mngr.IP.Equals(remoteClientIP) )
                    return true;
            return false;
        }

        private void BroadCastCommand(Command cmd)
        {
            foreach ( ClientManager mngr in this.clients )
                if ( !mngr.IP.Equals(cmd.SenderIP) )
                    mngr.SendCommand(cmd);
        }

        private void SendCommandToTarget(Command cmd)
        {
            foreach ( ClientManager mngr in this.clients )
                if ( mngr.IP.Equals(cmd.Target) )
                {
                    mngr.SendCommand(cmd);
                    return;
                }
        }
        private void UpdateConsole(string status , IPAddress IP , int port)
        {
            Console.WriteLine("Client {0}{1}{2} has been {3} ( {4}|{5} {6} )" , IP.ToString(),":" , port.ToString() , status,DateTime.Now.ToShortDateString(),DateTime.Now.ToLongTimeString(), "  IP Internet: " , IP.Address.ToString());
        }
        public void DisconnectServer()
        {
            if ( this.clients != null )
            {
                foreach ( ClientManager mngr in this.clients )
                    mngr.Disconnect();

                this.bwListener.CancelAsync();
                this.bwListener.Dispose();
                this.listenerSocket.Close();
                GC.Collect();
            }
        }
    }
}
