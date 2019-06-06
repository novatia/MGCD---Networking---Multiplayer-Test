using System.Net;
using UnityEngine;

using Unity.Networking.Transport;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine.Assertions;
using Unity.Networking.Transport.Utilities;

struct ServerUpdateConnectionsJob : IJob
{
    public UdpNetworkDriver driver;
    public NetworkPipeline pipeline;
    public NativeList<NetworkConnection> connections;

    public void Execute()
    {
        // CleanUpConnections
        for (int i = 0; i < connections.Length; i++)
        {
            if (!connections[i].IsCreated)
            {
                connections.RemoveAtSwapBack(i);
                --i;
            }
        }
        // AcceptNewConnections
        NetworkConnection c;
        while ((c = driver.Accept()) != default(NetworkConnection))
        {
            connections.Add(c);
            Debug.Log("Accepted a connection");
        }
    }
}

struct ServerUpdateJob : IJobParallelFor
{
    public UdpNetworkDriver.Concurrent driver;
    public NetworkPipeline pipeline;
    public NativeArray<NetworkConnection> connections;

    public void Execute(int index)
    {
        DataStreamReader stream;
        if (!connections[index].IsCreated)
            Assert.IsTrue(true);

        NetworkEvent.Type cmd;
        while ((cmd = driver.PopEventForConnection(connections[index], out stream)) !=
               NetworkEvent.Type.Empty)
        {
            if (cmd == NetworkEvent.Type.Data)
            {
                var readerCtx = default(DataStreamReader.Context);
                uint number = stream.ReadUInt(ref readerCtx);

                Debug.Log("Got " + number + " from the Client adding + 2 to it.");
                number += 2;

                using (var writer = new DataStreamWriter(4, Allocator.Temp))
                {
                    writer.Write(number);
                    driver.Send(pipeline, connections[index], writer);
                }
            }
            else if (cmd == NetworkEvent.Type.Disconnect)
            {
                Debug.Log("Client disconnected from server");
                connections[index] = default(NetworkConnection);
            }
        }
    }
}

public class JobifiedServerBehaviour : MonoBehaviour
{
    UdpNetworkDriver m_Driver;
    NetworkPipeline m_Pipeline;
    const int k_PacketSize = 256;
    public NativeList<NetworkConnection> m_Connections;
    private JobHandle ServerJobHandle;

    void Start()
    {
        m_Connections = new NativeList<NetworkConnection>(16, Allocator.Persistent);
        // Driver can be used as normal
        m_Driver = new UdpNetworkDriver(new SimulatorUtility.Parameters { MaxPacketSize = k_PacketSize, MaxPacketCount = 30, PacketDelayMs = 100 });
        // Driver now knows about this pipeline and can explicitly be asked to send packets through it (by default it sends directly)
        m_Pipeline = m_Driver.CreatePipeline(typeof(UnreliableSequencedPipelineStage), typeof(SimulatorPipelineStage));

        var endpoint = NetworkEndPoint.LoopbackIpv4;
        endpoint.Port = 9000;

        if (m_Driver.Bind(endpoint) != 0)
            Debug.Log("Failed to bind to port 9000");
        else
            m_Driver.Listen();
    }

    public void OnDestroy()
    {
        // Make sure we run our jobs to completion before exiting.
        ServerJobHandle.Complete();
        m_Connections.Dispose();
        m_Driver.Dispose();
    }

    void Update()
    {
        ServerJobHandle.Complete();

        var numConnections = m_Connections.Length;

        var connectionJob = new ServerUpdateConnectionsJob
        {
            driver = m_Driver,
            connections = m_Connections
        };

        var serverUpdateJob = new ServerUpdateJob
        {
            driver = m_Driver.ToConcurrent(),
            pipeline = m_Pipeline,
            connections = m_Connections.AsDeferredJobArray()
        };

        ServerJobHandle = m_Driver.ScheduleUpdate();
        ServerJobHandle = connectionJob.Schedule(ServerJobHandle);
        ServerJobHandle = serverUpdateJob.Schedule(numConnections, 1, ServerJobHandle);
    }
}
