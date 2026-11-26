using PlayRoom.Server;

return RoomKitApp.Run(args, PlayRoomApp.Options, PlayRoomApp.Map, new FactoryModule(PlayRoomApp.Jobs));
