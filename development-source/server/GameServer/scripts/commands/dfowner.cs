using DOL.GS.PacketHandler;
using DOL.GS.ServerRules;
using System;
namespace DOL.GS.Commands
{
    // GM level (GitHub #50): the launcher's "Make Me a GM" gives level 2, so as an Admin command nobody could run it
    // ("No such command"). It only says which realm may enter Darkness Falls: a GM testing tool like /jump.
    [Cmd("&dfowner", ePrivLevel.GM,
        "Changes the Realm owning access to Darkness Falls", "&dfowner <0-3 | none | alb | mid | hib>")]

    public class DFOwnerCommandHandler : AbstractCommandHandler, ICommandHandler
    {
        private static readonly Logging.Logger log = Logging.LoggerManager.Create(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public void OnCommand(GameClient client, string[] args)
        {
            if (args.Length == 2)
            {
                try
                {
                    var player = client.Player;
                    if (!TryParseRealm(args[1], out byte newRealm))
                    {
                        if (client != null && player != null) 
                            client.Out.SendMessage("Use /dfowner 0-3, or none, alb, mid or hib.", eChatType.CT_Important,
                                eChatLoc.CL_SystemWindow);
                        return;
                    }
                    DFEnterJumpPoint.SetDFOwner(player, (eRealm)newRealm);

                }

                catch (Exception)
                {
                    DisplaySyntax(client);
                }
            }
            else
                DisplaySyntax(client);
        }

        /// <summary>The realm a /dfowner argument names: 0-3, or none / alb(ion) / mid(gard) / hib(ernia).</summary>
        public static bool TryParseRealm(string text, out byte realm)
        {
            realm = 0;
            string t = (text ?? string.Empty).Trim().ToLowerInvariant();
            if (byte.TryParse(t, out byte number))
            {
                realm = number;
                return number <= 3;
            }
            switch (t)
            {
                case "none": realm = 0; return true;
                case "alb": case "albion": realm = 1; return true;
                case "mid": case "midgard": realm = 2; return true;
                case "hib": case "hibernia": realm = 3; return true;
                default: return false;
            }
        }

    }
}