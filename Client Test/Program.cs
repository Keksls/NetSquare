using System;
using System.Threading;

namespace Client_Test
{
    internal class Program
    {
        static void Main(string[] args)
        {
            for (int i = 0; i < 1000; i++)
            {
                Thread t = new Thread(() =>
                {
                    try
                    {
                        ClientRoutine client = new ClientRoutine();
                        client.Start();
                    }
                    catch (Exception ex)
                    { Console.WriteLine(ex); }
                });
                t.Start();
                Thread.Sleep(10);
            }
            Console.ReadKey();
        }
    }
}