KerbalLiveFeed Plugin, Client and Server v0.0.1
Created by Alfred Lam

======= Installation ===========================================

Place KLFClient.exe in your KSP install folder
Place KerbalLiveFeed.dll in your KSP plugin folder
Place the klfAntenna folder in your KSP parts folder

======= How to use ===========================================

Run KLFClient.exe while KSP is running and connect to a KLF server
To be able to see other ships in-game, you need to attach a KLF Communicator (Science category) to your ship

If you want ALL of your ships to be able to use KLF, even those without the klf part, add the following text to the part.cfg of all command pods, probe cores, and any part that can control a ship:

MODULE
{
	name = KerbalLiveFeedModule
}