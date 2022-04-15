using System;
using System.Threading;

namespace Client_Test
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("How many clients : ");
            string clients = Console.ReadLine();
            int nbCLients = 10;
            int.TryParse(clients, out nbCLients);
            Thread t = new Thread(() =>
            {
                for (int i = 0; i < nbCLients; i++)
                {
                    ClientRoutine routine = new ClientRoutine();
                    routine.Start();
                    Thread.Sleep(10);
                }
            });
            t.Start();
            Console.ReadKey();
        }
    }
}