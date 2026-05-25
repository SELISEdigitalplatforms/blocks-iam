import { HttpTransportType, HubConnection, HubConnectionBuilder } from "@microsoft/signalr";
import { BLOCKS_LOGIC_BASE_URL} from "@/constants/endpoint.constant";
import { getRuntimeEnv } from "@/lib/runtime-env";

export class NotificationClientService {
  public connection: HubConnection;

  constructor() {
    this.connection = new HubConnectionBuilder()
      .withUrl(
        `${BLOCKS_LOGIC_BASE_URL}/NotificationHub?x-blocks-key=${getRuntimeEnv("BLOCKS_X_BLOCKS_KEY")}`,
        {
          transport: HttpTransportType.WebSockets,
        },
      )
      .withAutomaticReconnect()
      .build();
    this.connect();
  }

  async connect() {
    this.connection.start();
  }

  async disconnect() {
    if (this.connection.state !== "Disconnected") {
      await this.connection.stop();
    }
  }
}

export const notificationClientService = new NotificationClientService();
