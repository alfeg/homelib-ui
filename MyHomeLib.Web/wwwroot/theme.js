window.myHomeLibTheme = {
    get: function () {
        return window.localStorage.getItem("myhomelib-theme");
    },
    set: function (value) {
        window.localStorage.setItem("myhomelib-theme", value);
    }
};

window.myHomeLibSession = {
    getOrCreateUserId: async function () {
        const response = await fetch("/api/session/user-id", {
            method: "GET",
            credentials: "include"
        });

        if (!response.ok) {
            return "";
        }

        const payload = await response.json();
        return payload?.userId ?? "";
    }
};
