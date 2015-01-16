
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;

using Autodesk.Connectivity.Explorer.Extensibility;
using Autodesk.Connectivity.Extensibility.Framework;
using Autodesk.Connectivity.WebServices;
using Autodesk.Connectivity.WebServicesTools;
using VDF = Autodesk.DataManagement.Client.Framework;
using Autodesk.Connectivity.JobProcessor.Extensibility;


//There are  5 assembly attributes you need to have in your code:
//Items 1-3 are provided by Visual Studio in the AssemblyInfo file.  
//You just need to check that they have accurate data.  
//Items 4 and 5 need to be created by you.

//[assembly: AssemblyCompany("Your Company")]
//[assembly: AssemblyProduct("FourEyes")]
//[assembly: AssemblyDescription("Your assembly description")]
//For Autodesk Vault 2014, ApiVersion is "6.0"
[assembly: ApiVersion("6.0")]
[assembly: ExtensionId("ded8a16c-842b-461f-bf19-44839fd50a05")]

namespace FourEyes
{
    //if you rename this class name, please make sure change accordingly in vaultplugin.vcet.config
    public class Class1 : IWebServiceExtension
    {
        #region "FourEyes"
        #region IWebServiceExtension implementation
        public void OnLoad()
        {
            DocumentServiceExtensions.UpdateFileLifecycleStateEvents.GetRestrictions += new EventHandler<UpdateFileLifeCycleStateCommandEventArgs>(UpdateFileLifecycleStateEvents_GetRestrictions);
        }
        #endregion
        // event handler
        
        private long relProcess = 0;

        void UpdateFileLifecycleStateEvents_GetRestrictions(
            object sender, UpdateFileLifeCycleStateCommandEventArgs e)
        {
            try
            {
                IWebService service = sender as IWebService;
                if (service == null)
                    return;
                WebServiceCredentials cred = new WebServiceCredentials(service);
                //WebServiceCredentials_bugfix cred = new WebServiceCredentials_bugfix(service);
                using (WebServiceManager mgr = new WebServiceManager(cred))
                {
                    long currentUserId = mgr.SecurityService.SecurityHeader.UserId;
                    FileArray[] fileCollection = mgr.DocumentService.GetFilesByMasterIds(e.FileMasterIds);
                    //checks the lifecycle of the first file we are changing the state of.
                    CheckFile(fileCollection[0].Files);

                    LfCycDef[] defs = mgr.DocumentServiceExtensions.GetAllLifeCycleDefinitions();
                    //LfCycDef releaseProcess = defs.FirstOrDefault(
                    //    n => n.SysName == "Flexible Release Process");

                    LfCycDef releaseProcess = defs.FirstOrDefault(n => n.Id == relProcess);
                    //LfCycDef releaseProcess = defs.FirstOrDefault(n => n.SysName == "Basic Release Process");

                    LfCycState reviewState = null;
                    LfCycState releaseState = null;

                    switch (releaseProcess.DispName)
                    {
                        case "Basic Release Process":
                            reviewState = releaseProcess.StateArray.FirstOrDefault(n => n.DispName == "For Review");
                            releaseState = releaseProcess.StateArray.FirstOrDefault(n => n.DispName == "Released");
                            break;
                        case "Legacy Release Process":
                            reviewState = releaseProcess.StateArray.FirstOrDefault(n => n.DispName == "For Review");
                            releaseState = releaseProcess.StateArray.FirstOrDefault(n => n.DispName == "Released");
                            break;
                        case "Project Release Process":
                            reviewState = releaseProcess.StateArray.FirstOrDefault(n => n.DispName == "Internal Review");
                            releaseState = releaseProcess.StateArray.FirstOrDefault(n => n.DispName == "Customer Approval");
                            break;
                        case "Matrix Release Process":
                            reviewState = releaseProcess.StateArray.FirstOrDefault(n => n.DispName == "For Review");
                            releaseState = releaseProcess.StateArray.FirstOrDefault(n => n.DispName == "Sign & Release");
                            break;
                        default:
                            break;
                    }

                    for (int i = 0; i < fileCollection.Length; i++)
                    {
                        CheckFile(fileCollection[i].Files, e.ToStateIds[i],currentUserId, reviewState, releaseState, e);
                    }
                }
            }
            catch { }
        }

        private void CheckFile(File[] files)
        {
            // we don't care about states or anything else at this point only that the latest version of the file(s) selected have a lifecycle assigned to them and what it is.
            File maxFile = files.First(n => n.MaxCkInVerNum == n.VerNum);
            if (maxFile.FileRev == null)
                return;

            // gather all the files in the revision and arrange them by version
            IEnumerable<File> filesInRev =
                from n in files
                where n.FileRev.RevId == maxFile.FileRev.RevId
                orderby n.VerNum
                select n;

            File[] filesArray = filesInRev.ToArray();

            for (int i = 1; i < filesArray.Length; i++)
            {
                File f2 = filesArray[i];
                if (f2.FileLfCyc != null)
                {
                    // f2 is a version where the state changed
                    relProcess = f2.FileLfCyc.LfCycDefId;
                    continue;
                }
            }
        }

        // checks for a four eyes violation for a given file history
        private void CheckFile(File[] files, long toStateId,
            long currentUserId, LfCycState reviewState,
            LfCycState releaseState, WebServiceCommandEventArgs eventArgs)
        {
            // if we are not moving to released, don't event bother with the check
            if (toStateId != releaseState.Id)
                return;

            File maxFile = files.First(n => n.MaxCkInVerNum == n.VerNum);
            if (maxFile.FileRev == null)
                return;

            // gather all the files in the revision and arrange them by version
            IEnumerable<File> filesInRev =
                from n in files
                where n.FileRev.RevId == maxFile.FileRev.RevId
                orderby n.VerNum
                select n;

            File[] filesArray = filesInRev.ToArray();

            long reviewUserId = -1;
            for (int i = 1; i < filesArray.Length; i++)
            {
                File f1 = filesArray[i - 1];
                File f2 = filesArray[i];

                // compare two concecutive file versions to determine 
                // where a state changed happened
                if (f1.FileLfCyc != null && f2.FileLfCyc != null &&
                    f1.FileLfCyc.LfCycStateName != f2.FileLfCyc.LfCycStateName &&
                    f2.VerNum - f1.VerNum == 1)
                {
                    // f2 is a version where the state changed
                    if (f2.FileLfCyc.LfCycStateName == reviewState.DispName)
                        reviewUserId = f2.CreateUserId;
                }
            }

            if (reviewUserId > 0 && currentUserId == reviewUserId)
            {
                // the same person reviewed the file in an earlier version
                eventArgs.AddRestriction(
                    new ExtensionRestriction(maxFile.Name,
                    "File cannot be reviewed and released by the same person"));
            }
        }
        #endregion
    }
}
