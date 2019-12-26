﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AncestryDnaClustering.Models;
using AncestryDnaClustering.Models.HierarchicalClustering;
using AncestryDnaClustering.Models.SavedData;
using AncestryDnaClustering.Properties;

namespace AncestryDnaClustering.ViewModels
{
    internal class AncestrySavedDataExtender
    {
        private readonly AncestryMatchesRetriever _matchesRetriever;
        private readonly ProgressData _progressData;

        public AncestrySavedDataExtender(AncestryMatchesRetriever matchesRetriever, ProgressData progressData)
        {
            _matchesRetriever = matchesRetriever;
            _progressData = progressData;
        }

        public async Task ExtendSavedDataAsync(string guid)
        {
            var serializedMatchesReaders = new List<ISerializedMatchesReader>
            {
                new DnaGedcomAncestryMatchesReader(),
                new DnaGedcomFtdnaMatchesReader(),
                new SharedClusteringMatchesReader(),
                new AutoClusterCsvMatchesReader(),
                new AutoClusterExcelMatchesReader(),
            };

            var matchesLoader = new MatchesLoader(serializedMatchesReaders);

            Settings.Default.Save();

            var startTime = DateTime.Now;

            var (testTakerTestId, clusterableMatches, tags) = await matchesLoader.LoadClusterableMatchesAsync(@"C:\Users\JSB\Desktop\Ancestry\bin\Debug\jim-ostroff-icw-saved-170000-200.txt", 6, 6, _progressData);
            if (clusterableMatches == null)
            {
                return;
            }
            var matches = clusterableMatches.Where(match => _toExtend.Contains(match.Match.TestGuid)).ToList();

            // Make sure there are no more than 100 concurrent HTTP requests, to avoid overwhelming the Ancestry web site.
            var throttle = new Throttle(100);

            // Don't process more than 50 matches at once. This lets the matches finish processing completely
            // rather than opening requests for all of the matches at onces.
            var matchThrottle = new Throttle(50);

            // Now download the shared matches for each match.
            // This takes much longer than downloading the list of matches themselves..
            _progressData.Reset($"Downloading shared matches for {matches.Count} matches...", matches.Count);

            var matchIndexes = clusterableMatches.ToDictionary(match => match.Match.TestGuid, match => match.Index);

            var icwTasks = matches.Select(async match =>
            {
                await matchThrottle.WaitAsync();
                var result = await _matchesRetriever.GetMatchesInCommonAsync(guid, match.Match, false, 6, throttle, matchIndexes, _progressData);
                var coords = new HashSet<int>(result)
                {
                    match.Index
                };
                return new
                {
                    Index = match.Index,
                    Icw = coords,
                };
            });
            var icws = await Task.WhenAll(icwTasks);

            var icwDictionary = icws.ToDictionary(icwTask => icwTask.Index, icwTask => icwTask.Icw);

            var updatedClusterableMatches = clusterableMatches.Select(match => icwDictionary.ContainsKey(match.Index) ?
                new ClusterableMatch(match.Index, match.Match, icwDictionary[match.Index]) : match).ToList();

            // Save the downloaded data to disk.
            _progressData.Reset("Saving data...");

            var icw = updatedClusterableMatches.ToDictionary(
                match => match.Match.TestGuid,
                match => match.Coords.ToList());

            var output = new Serialized
            {
                TestTakerTestId = guid,
                Matches = updatedClusterableMatches.Select(match => match.Match).ToList(),
                MatchIndexes = matchIndexes,
                Icw = icw
            };
            var fileName = @"C:\Users\JSB\Desktop\Ancestry\bin\Debug\jim-ostroff-icw-saved-extended-170000-200.txt";
            FileUtils.WriteAsJson(fileName, output, false);

            var matchesWithSharedMatches = output.Icw.Where(match => match.Value.Count > 1).ToList();
            var averageSharedMatches = matchesWithSharedMatches.Sum(match => match.Value.Count - 1) / (double)matchesWithSharedMatches.Count;
            _progressData.Reset(DateTime.Now - startTime, $"Done. Downloaded {matches.Count} matches ({matchesWithSharedMatches.Count} with shared matches, averaging {averageSharedMatches:0.#} shared matches");
        }

        private HashSet<string> _toExtend = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
"4E6B58A8-B6DE-417F-A75A-DF648ADA8E59",
"0EB8B69C-2C05-4412-80AB-7AF57494D924",
"B54B84EC-58BE-41B4-B120-1921AE73A0A3",
"34286683-2A0B-4DD2-9F5E-DEF09EE4F2EB",
"15DF41B9-D31A-42ED-96E3-FCF532E2875F",
"2EC18A0A-168A-48BA-BA14-EED2BBF95B0F",
"5CDC131F-5A7F-4FDD-B14A-433DF76DC421",
"32D44CFB-4973-4AAF-B380-C7AA66A9AD29",
"13396104-4E06-4A6F-9303-AFCE41CF3848",
"4A0DB1FB-DAC9-4EDB-811C-83E3A1AFE7ED",
"5675BDA9-4733-4AEC-90CC-B4109AE4ABB6",
"BF1FC9EF-EFE4-4DAD-86FC-979BBCAE16F3",
"24633916-35A5-4CD0-8219-06BD0CE78665",
"DF23ECDC-20C6-445A-BDD9-976D043B8B46",
"5D0595F8-F2F9-4170-9A39-83FC074C79E8",
"57CB7A50-083C-4BB7-9C29-BAAB0C783336",
"111864AE-75E8-4915-8784-98B6F45C7B37",
"C718A664-078A-4AE0-B469-4A659D1EB96B",
"555EA083-51F8-4790-B88D-BCD221C85E16",
"770E297E-7340-4008-8161-FC4B80CD9B9F",
"0C51AE0C-69B7-4A3B-8A29-0BBB84CBD697",
"76F83351-FAB2-4D07-A0B5-CD59D66F269A",
"35E1BEB4-79FA-4638-B26A-E765BEEDCF0C",
"8B808455-7F4E-4A5C-883D-B93868D50536",
"6063A05A-364F-4C57-945F-2CF5D081ABDA",
"A029CC91-4F3E-4106-984F-6819529E671D",
"FF0E2FA9-589E-43E6-AF66-5CDDEC311BC2",
"52214929-70DD-4DBC-8D66-AC4E536F88E4",
"634D613A-E8AF-4341-A656-91A065F0486F",
"D3BA4FB7-BADE-4B46-955C-6B94BA96A42E",
"F6F4FBE9-8982-4906-B271-60AB32C02B8A",
"6483BCBD-7484-4994-B046-99B9356AF481",
"D4D1C310-794A-456D-BED0-7D0CB5546894",
"1D4FCDD5-CA92-4A3F-938E-DED7C17C9E77",
"FDA1DA4D-30BD-4736-B29B-C9122519CD69",
"0C45B243-2786-45F3-972F-E28C8A5C2FB7",
"890FA9B5-6237-48D2-862F-6DB474DB5646",
"9BAD04FD-A524-4639-9159-3026E5968714",
"AB276C2F-29BC-4BC6-BF9E-6055ED1BA14D",
"F7D39F18-47E6-4294-A267-3159D9E9BD0B",
"50FD4979-E351-4E1E-95FD-E595F6E9BF96",
"42A2754A-DE54-4C90-82CE-2078F58A8B61",
"85B61877-E711-4ABC-B978-B990017450F3",
"F5760E97-380E-4B54-B59F-7868BE6A225B",
"D595E34E-DB1B-47D8-9BDD-CECE24EBB4F0",
"453B9920-C8CF-4EF5-BEFA-D454038C3130",
"A8703A3C-E749-435F-8C10-BF65B6F7DC7C",
"155D418D-F21B-4278-AD4A-57DFE3E80D60",
"EA035072-9110-46F0-9CF5-4CC11024C7C3",
"D08A58AE-08DB-4C35-A6CA-481EC19B6338",
"06385EC3-E76E-42C7-A082-E795C7DBC6B1",
"62CD40BC-BA63-47F6-B376-3897848897B2",
"186BFAD1-11A2-47AC-80CD-46F0A6BDDFAE",
"4FB8E246-4985-492A-B722-68C9F406465A",
"18A4B19D-C10C-4CC4-9AC9-03C5638C5388",
"7701A490-45E9-40DC-87D7-65124F69EBA3",
"6C2BC3BF-F127-470D-BF79-2498D4B02F1D",
"5F6D192E-F388-4318-8749-078355575037",
"B6CD0BA3-DE6E-436A-ADBF-820D8FE7CA04",
"BB30DD26-7D53-415E-9762-2F24A6D1FB3E",
"1EEB971F-7D68-47B0-924C-8AA591B4288F",
"4384E820-FD03-4259-A477-27111ADD78FE",
"FDF5AB08-B77A-4050-8077-18AC96B528A8",
"9A2F9FB9-35B6-40A5-90B3-F56A700C4F36",
"66F80EE3-1608-4318-AF1B-E27004E6AB89",
"0840FCDE-4E08-4B4B-A335-DF6A66614214",
"65DBAEFF-5DD2-4B39-9708-C79823ED233E",
"18E3E5BE-AABC-4AD9-ADA6-87A3F9ECBC10",
"CE788020-DFC1-490C-9659-784AAD7BA0E1",
"9D5B3ACB-D11E-4AA4-8B21-D911ACD921EC",
"2ADF6FE5-BEF8-44F9-82EF-924473690FF5",
"85458887-DD87-4CE2-B81F-D551DAABFEFE",
"61EA573C-F446-4F22-9886-6C8EE4756A64",
"F31579DA-44CA-458A-ADD8-F6D91D50E791",
"E7C2343C-7B18-436D-AF63-98299A8857E7",
"1465176B-BF00-4986-A7EB-0A5EE6A445E3",
"4041BFB5-2647-479F-BB22-4D1670FE9F9C",
"B0E58234-C0BC-49FF-B090-43C790F2637B",
"C5879ABF-7011-41E1-A540-3BF0975B0045",
"A43A3F56-6A24-4495-8560-96678B5E8CF2",
"725D742A-2A93-40EA-8E24-C5EAB69B1B04",
"16BFA853-ED67-4747-AD26-05834C1CE53E",
"EB09E365-6436-4E51-87A9-C4F671BF20D0",
"DEF7A26B-1CF9-4F16-9024-33714F0C602E",
"0D82A52A-7EC1-4622-8724-8E112DBB8721",
"D409629A-DF8E-4431-AA61-BCB681990181",
"5E5CFB71-504A-4E87-8422-05CAC0FDCAD0",
"27A12469-19EB-4B51-8ED8-36E36CAE0FEA",
"88078D22-60A7-4D65-A1B3-B9B5227C3659",
"32958D29-8BF3-4D0D-99F8-D092527C022D",
"18BFB432-3589-4001-A68A-576B1BB9B157",
"1E5ED984-966A-4FDF-A8D7-32BD80A6FE83",
"57E0F91C-4719-483E-A727-3C52F03E552A",
"98D3A61B-3512-46E1-B237-68C752E635C0",
"D6AA4F38-810A-4AC0-A615-CFC1D8BEA95A",
"19590952-85C2-44E0-986B-48E88F030322",
"C6A0F423-D027-416B-8D51-175165578D16",
"5D8AE30F-3464-4A3C-94D1-CFC268AD1388",
"8B3603CD-2346-49BB-97A4-B778C419D5BA",
"11A96130-FC12-491A-81D6-2B5422A5E67D",
"9ADDC38F-5BC7-4E6C-970E-62674CF6FB55",
"B7649B2E-A185-48D2-A366-7F244A3AE706",
"C887149C-C5B0-4405-83EE-7AFEE22921EA",
"54918441-8432-4AC2-B4F0-68FE3BA81D65",
"21C12341-5108-4C88-A8F8-884640BCECB7",
"2897A8E6-71FA-4101-A468-94C97AD87CC8",
"81442C53-FB4C-4835-A95C-75F8BCA4F62C",
"B37D3D85-B979-48E2-A8D1-EB4CE21DC898",
"8392BEF6-0AE4-4119-B5B2-BF7788C707E1",
"966F7C32-E319-4A9B-8835-9AA762E2B6A9",
"F409438F-8AF4-46B5-B4D1-D39B51C5F0BE",
"346B555C-A2C0-4DD4-96F2-87FBC936A1A0",
"17386050-DE47-409E-9CD2-B69BE87E2DE1",
"7CECACBF-8211-41EC-86D8-1C69E8D2DB8F",
"C1B97A15-3DA9-4FC1-9273-F02EC9381434",
"96C5EDF8-CAA2-480C-B0CC-119AE6DCA3F9",
"ECCB0E0B-C15B-4617-91B5-AE0CD976AB92",
"E220B359-392B-40CD-B255-434C46443042",
"F85E8541-8207-40D6-AB7E-6EEC30F6410F",
"273BB64F-7CB0-4403-8A5E-F8C0B8DF22C7",
"1D3F6436-528A-43A3-8454-D16B027D5599",
"6360C3C3-3990-41C5-9418-B27C249B5F42",
"7C869176-FD08-4F6F-9495-AA7B6083A43E",
"E72FE328-79AF-42CB-BFB3-D1631D9807F4",
"E0295427-2079-4446-B579-C2FAB4249EA1",
"0950D1AE-4E67-4832-A485-3222F6A5049E",
"2E4C71EE-55ED-4AD5-B5FD-3C3368C14220",
"83B270DF-6FB5-4C5D-98F4-FA280C709A30",
"91CBEBC8-CC25-46DB-9AAD-0D34492D18F0",
"B2BB25A4-F745-4D03-B713-01BDB134ED60",
"20B4804B-E1D7-4AD4-9C17-748B9FF17ADA",
"FDABA65D-BE67-4E80-9C71-4F20A4618CDC",
"EE8B86E6-B50E-4A24-8DC0-A863474F2FFE",
"502561F4-67A6-467F-BE73-2BF02A4F3AAB",
"02DE99E4-B179-4F8E-95FE-1AFC16670CCC",
"5734ECDD-72AC-479F-A2E2-19DC1A6068F0",
"C19BB81D-ED8F-4902-A23C-C93DD5DEA6DC",
"028AFC28-1987-4739-A0D7-1371D8DFD057",
"F392587D-27B0-497D-81AF-7EB0EE28984D",
"D46678A3-4505-4EB4-BBA6-42BF89C9CCC1",
"83EBB489-26A2-4730-8426-45965C48EAC7",
"90DC9F70-B908-4006-AB3F-CE8719282B72",
"5A20DAE5-BF8C-4C10-8E3B-F40180F24AC8",
"A67AD11D-85FB-41E7-A0A9-609DA80B8B24",
"C2464F5E-2CAC-4E7E-933B-AD664CE4934B",
"F79D6FB0-DE0A-444C-B5B1-7D93764D9B8F",
"A57B0B34-B5C1-416E-B099-2F45BFEC9E2E",
"AD8767CF-9B43-4B5D-AFCE-19313AB28BE4",
"93F59BB0-6098-4B4B-B261-AF4615AA825F",
"5EB20498-72FF-4453-BAF2-A3FE42B69E41",
"924D264D-03FF-466F-9526-BDC500CA7E29",
"98BBF49E-F4DC-43EB-B2B4-BA04A0558A3F",
"4D1B4252-2667-40B8-8167-234BED8599EC",
"0C60534D-164F-48DA-8356-96591EB9AC11",
"EE174B6F-F2DC-4229-9BA0-1CE393528F8E",
"39B76CB4-741A-4DE7-9FB5-CD07508AD0EF",
"28F32AA9-F914-4F11-9160-22A60A26D76B",
"8547409F-6A59-4B21-8ADD-0B973691D73B",
"2251C00A-65E2-4B3D-955C-64A9D7AF9027",
"45B3A187-A288-4A5B-8D6C-7568CC6D903B",
"25389733-A544-4D0C-8399-AD19D0B061DB",
"0D07BA18-DA25-4F29-AC2E-ED59614AF75C",
"761C73C9-BB58-49BC-AFD1-9A070C275248",
"898BA9DC-2AB4-494A-BB81-BC88ED01C83F",
"0D709485-FD4B-471C-9A6D-37DAA0A5068B",
"C24515C5-9071-4C07-85C2-30EDBE1ECE7F",
"C0A31CD1-BBBC-47C0-B267-DDA4F974C256",
"944C9F4E-1BF0-4B5C-955A-71DD0CCA1339",
"776893C6-9511-4F80-8D44-B28A18F5F4EE",
"CE93CD2F-727C-4A3F-A26D-C28CE79DC65B",
"97FB90C3-901A-4263-988D-8C595E2B570F",
"8E209CDB-2A76-417D-8948-C3B2FCA5848F",
"2EE0DA45-0D4C-49F2-BD3F-EE3B0C9EC5C2",
"6D904E5B-81A2-4FF1-BFDB-C7AEE2002A2A",
"96D5D7ED-8572-45D0-B89B-8DF3A46D5A6A",
"FAFAF5EC-4B09-4850-9FAB-35E48AF23504",
"D2E87496-51B1-4C9B-B626-4074A0E6EB57",
"E3E3F632-7F01-4529-B368-9EB28B9CDC92",
"74334FFC-DBFF-4C2B-9643-BF59AEEF8441",
"BEE87D9A-1F18-4360-8795-DEB5650D3204",
"9C006735-B3E0-42CC-AF3A-92775799E6D4",
"A7C83934-9D0F-4108-BADC-5E01B33F8CD4",
"4F6E7ED6-31B3-426D-85BD-993C25C87410",
"83F4EE50-BECB-4EFA-90D1-8E0917E12B22",
"71012673-06AB-47D5-887D-78B253A3A909",
"1569F363-1C09-45C8-8250-5326E2DFA074",
"340A04C0-37B1-4400-8685-54A63558A77B",
"7033E3E3-3089-4465-ACDF-9537B6417817",
"92C397F5-0EBA-4B1B-99D7-FC8CCDA9268F",
"7C751BA6-BB9F-45F9-B9E4-50B55EF9B0ED",
"FEA327F6-8979-4998-AB17-4943A30944CD",
"3E29FC35-B1AB-4C77-831E-072B380AF2CB",
"D23C81BB-7B52-4DB7-94C0-F0A1768F5C4A",
"EA92CEA4-E45F-4B37-A387-EF23E9BCF433",
"C6168FA0-BDD4-4A39-987B-7223D4D7CBBF",
"2F5F2739-C8FC-447D-8063-C52286D111A0",
"C13182E4-623D-4403-92D2-A103B66FCA77",
"F8215FCD-1037-4777-987D-A66924389D59",
"C8C37752-C12E-4796-9548-2C6CA4308D88",
"A00BAA09-29B4-4DF0-BA02-003FCD8151ED",
"76FC23BA-431C-4BED-A0B5-5243467B11A0",
"052418BB-1980-41BD-996A-D5F2160C5B3C",
"2362E039-8634-4E7D-9074-FA4290B6610D",
"71C41A10-2519-4A41-85CB-D2CF402E9B7C",
"EA95508F-BBBB-4233-81E0-EE64F8FF4E1F",
"7E5124F4-A033-4428-87B1-8ECE8D089C2F",
"C6CA6DE5-ADC4-494B-87D5-EFA3165323CB",
"D7E70304-6352-4703-B649-10F353F95C49",
"4037CA35-E00D-41C0-AC7D-5F2E9B2C52F7",
"021976F5-E2C7-46BA-9941-2C6B3BCA5A47",
"13B809CA-3683-43A3-B72D-749082A605B9",
"2D0FE59F-29A5-4AD6-A08F-F6ED74C74503",
"71C2C1BF-737F-4DF0-AD70-B3FB9E8D3EFC",
"B69E9E30-391A-4A2C-B43C-C46564CB8B35",
"424AB7A9-B943-4D08-9AF6-9B0816A543A0",
"C2FA2CFB-BE6E-4237-BB16-731832159AF4",
"0ED36DD6-7C0F-429A-9FBE-FC63BB5B2E55",
"E15CE4C4-8348-4E45-ACC6-B3472DC11746",
"0DDB4C3A-AE2A-493A-B756-A26667C95994",
"A202C0C9-4B69-4CA3-8FE0-EA1F2623DE67",
"08A10927-2339-4CF5-A346-B95B7DA4A352",
"6DDE65DB-9F1A-4418-9953-D9D13C737A43",
"2E26F82D-1D5F-432C-917B-3208C0BA597F",
"212E76E4-5FAF-43A2-B0E9-2951E7026F27",
"F08EA44E-4E09-4B38-ABA3-AF9FAAC5493E",
"9948D8AF-41E1-491A-A9C7-8E295C98C7F4",
"A3034767-9E6F-4047-961F-83199F730899",
"B6D73B4C-70F8-4862-BB72-473D43916B50",
"983D5FB5-1831-4062-8B45-491DF8FCB63D",
"1869957A-8374-4445-9A0E-D066850A6D1C",
"89BDE2DF-5523-4F1C-B576-B62219C68B21",
"522595B0-80FB-4464-A27B-88A98744C15A",
"C9581D25-93F0-4E3D-AF39-EAC5338A53C1",
"F42C4E5F-3F04-467C-A629-191592A8A2A5",
"9ECA0A7F-E416-46CC-A47E-F8E1365E2EAE",
"A0C8172D-8D96-4931-BF82-B3C93B89390E",
"4D720858-222E-478D-8918-F642E1488D95",
"A3E3A3C5-687A-4D09-BF31-3E214EA26A1C",
"0E49E199-9011-4F03-BA95-137D953A8630",
"0EE7D123-6EC2-4758-B665-2FA49DE1DCAE",
"BAAF46E4-21D5-4CCC-BE35-170F2BAD1DE4",
"BBB3EDF1-960C-4E44-A6C0-EA0908683B52",
"02B3FDA7-BFE7-4F34-8A13-AD7CE408ADCD",
"5D4BB62F-EF3F-4132-BF85-106086887FB4",
"83758948-D544-45E1-83DF-BE73653EAD4B",
"652374E6-D237-4D5A-818B-F3ADF7D856F9",
"EEE0608D-909C-4800-9403-DD644788F52C",
"D833F6C6-C36C-4A96-9AFB-58086C26993A",
"E7CEDB71-ADAD-4094-B109-6D0A13F3A559",
"42897CEB-917F-46BD-A17A-15B98AAC0ADD",
"1873380D-7272-4905-923A-02B4A4C3F139",
"75B314B2-EA4E-4719-8561-6BC830515356",
"BD93AB05-78BC-4D42-9CCB-21AB8D82F51C",
"67323991-4415-4142-A050-BD828F961499",
"4750B48D-E295-4A9F-B94F-54E7B754EE2D",
"71DF6048-E756-4E8B-9A0F-4FB08F9E5EE4",
"2722DD48-7FC7-47B7-947D-A23EF7666BB9",
"4B16053D-3DC7-4AC9-80FA-7B007E1B45E2",
"E3980623-B44B-4A45-8977-391A20139B7A",
"59203794-90AA-4345-81F1-D1895155339C",
"B83F366E-F46D-437F-94E6-C77C67E34C60",
"7BAE043F-725A-41DE-A53C-3F1B18559082",
"AEC08BA8-8266-423F-858A-A20C38521A16",
"23A6207B-6918-4733-B9F2-11D15356C375",
"34A27A13-271F-4039-8EA2-5273B0723FEF",
        };
    }
}
