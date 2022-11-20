using System;
using System.Collections.Generic;
using System.Threading;

namespace Client_Test
{
    internal class Program
    {
        static void Main(string[] args)
        {
            SlyvekClientRoutine scr = new SlyvekClientRoutine();
            scr.Start();
            Console.ReadKey();
            return;

            Console.WriteLine("How many clients : ");
            string nbClients = Console.ReadLine();
            int nbCLients = 10;
            int.TryParse(nbClients, out nbCLients);
            List<ClientRoutine> clients = new List<ClientRoutine>();

            Thread t = new Thread(() =>
            {
                for (int i = 0; i < nbCLients; i++)
                {
                    ClientRoutine routine = new ClientRoutine();
                    routine.Start();
                    clients.Add(routine);
                    Thread.Sleep(1);
                }
            });
            t.Start();

            Thread s = new Thread(() =>
            {
                while (true)
                {
                    for (int i = 0; i < clients.Count; i++)
                        clients[i].TestSync();
                    Thread.Sleep(100);
                }
            });
            s.Start();
            Console.ReadKey();
        }
    }
}