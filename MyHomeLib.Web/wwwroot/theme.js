window.myHomeLibTheme = {
    get: function () {
        return window.localStorage.getItem("myhomelib-theme");
    },
    set: function (value) {
        window.localStorage.setItem("myhomelib-theme", value);
    }
};

window.myHomeLibSession = {
    getUserId: function (cookieName) {
        const name = cookieName + "=";
        const parts = document.cookie.split(";");
        for (let i = 0; i < parts.length; i++) {
            const cookie = parts[i].trim();
            if (cookie.startsWith(name)) {
                return decodeURIComponent(cookie.substring(name.length));
            }
        }
        return "";
    }
};
