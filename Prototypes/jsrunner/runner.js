(function () {
	var fileFetcher = function (filename, onSuccess, onFailure) {
		$.ajax({
			url: filename,
			success: onSuccess,
			error: onFailure
		});
	};
	
	var binaryFileFetcher = function (filename, onSuccess, onFailure) {
		var xhr = new XMLHttpRequest();
		xhr.open('GET', filename, true);
		xhr.responseType = 'arraybuffer';
		xhr.onload = function() {
			if (this.status == 200) {
				var result = new Uint8Array(this.response);
				onSuccess(result);
			}
			else {
				onFailure();
			}
		};
		xhr.send();
	};
	
	var checkCanSave = function () {
		$.ajax({
			url: apiRoot + "games/cansave",
			success: function (result) {
				if (result) {
					$("#cmdSave").show();
				}
			},
			xhrFields: {
				withCredentials: true
			}
		});
	};
	
	var launchV6 = function (data) {
		quest.load(data);
		quest.begin();
	};
	
	window.gridApi = window.gridApi || {};
	window.gridApi.onLoad = function () {
		var id = $_GET['id'];
		var resume = $_GET['resume'];
		
		// Local testing only =========
		
		var filename = $_GET['file'];
		$.get(filename, function (data) {
			launchV6(data);
			return;
		});
		
		// End local testing only =====
		
		if (!id) return;
		
		var load = function () {
			$.get('http://textadventures.co.uk/api/game/' + id, function (result) {
				checkCanSave();
				
				if (result.ASLVersion >= 500) {
					$.get(result.PlayUrl, function (data) {
						// TODO: Pass result.ResourceRoot
						launchV6(data);
					});
				}
				else {
					var file = result.PlayUrl;
					var game = new LegacyGame(file, file, resumeData, fileFetcher, binaryFileFetcher, result.ResourceRoot);
					var onSuccess = function () {
						game.Begin();
					};
					var onFailure = function () {
						console.log("fail");
					};
					quest.sendCommand = game.SendCommand.bind(game);
					quest.endWait = game.EndWait.bind(game);
					quest.setQuestionResponse = game.SetQuestionResponse.bind(game);
					quest.setMenuResponse = game.SetMenuResponse.bind(game);
					quest.save = game.SaveGame.bind(game);
					quest.tick = game.Tick.bind(game);
					game.Initialise(onSuccess, onFailure);
				}
			});
		};
		
		var resumeData = null;
		
		if (!resume) {
			load();
		}
		else {
			$.ajax({
				url: 'http://textadventures.co.uk/games/load/' + id,
				success: function(result) {
					resumeData = atob(result.Data);
					load();
				},
				error: function() {
					// TODO: Report error to user
				},
				xhrFields: {
					withCredentials: true
				}
			});
		}
	};
	
	// TODO: Game session logging for ActiveLit
	// if (gameSessionLogId) {
    //     $.ajax({
    //         url: apiRoot + "games/startsession/?gameId=" + $_GET["id"] + "&blobId=" + gameSessionLogId,
    //         success: function (result) {
    //             if (result) {
    //                 gameSessionLogData = result;
    //                 setUpSessionLog();
    //             }
    //         },
    //         type: "POST",
    //         xhrFields: {
    //             withCredentials: true
    //         }
    //     });
    // }
})();