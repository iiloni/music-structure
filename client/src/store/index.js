import Vue from "vue";
import Vuex from "vuex";
import Track from "../app/Track";
import * as log from "../dev/log";

Vue.use(Vuex);

export default new Vuex.Store({
    state: {
        browser: "",
        user: Object,
        trackList: [],
        selectedIndex: -1,
        loadingTrack: true,
        // Player global variables
        playerReady: false,
        playing: false,
        seeker: 0,
        // For every worker we need a global ready variable
        tsneReady: false,
        ssmReady: false,
        clusterReady: false,
    },
    mutations: {
        setSeeker(state, time_ms) {
            state.seeker = time_ms;
        },
        setUser(state, user) {
            state.user = user;
        },
        setPlayerState(state, playerState) {
            state.playerState = playerState;
        },
        loadingTrack(state, loadingTrack) {
            state.loadingTrack = loadingTrack;
        },
        playerReady(state, playerReady) {
            state.playerReady = playerReady;
        },
        addToTrackList(state, track) {
            state.trackList.push(track);
        },
        addToTrackListIndex(state, index, track) {
            state.trackList.splice(index, 0, track);
        },
        addToTrackListFront(state, track) {
            state.trackList.unshift(track);
        },
        clearTrackList(state) {
            state.trackList = [];
        },
        setSelectedIndex(state, index) {
            state.selectedIndex = index;
        },
        setPlaying(state, playing) {
            state.playing = playing;
        },
        incrementSeeker(state, increment) {
            state.seeker += increment;
        },
        tsneReady(state, ready) {
            state.tsneReady = ready;
        },
        clusterReady(state, ready) {
            state.clusterReady = ready;
        },
        ssmReady(state, ready) {
            state.ssmReady = ready;
        },
    },
    actions: {},
    getters: {
        seeker(state) {
            return state.seeker;
        },
        selectedIndex(state) {
            return state.selectedIndex;
        },
        selectedTrack(state) {
            return state.trackList[state.selectedIndex];
        },
        user(state) {
            return state.user;
        },
        track(state, id) {
            return state.trackList[id];
        },
        trackList(state) {
            return state.trackList;
        },
        playerState(state) {
            return state.playerState;
        },
        playerReady(state) {
            return state.playerReady;
        },
        playing(state) {
            return state.playing;
        },
        tsneReady(state) {
            return state.tsneReady;
        },
        clusterReady(state) {
            return state.clusterReady;
        },
        ssmReady(state) {
            return state.ssmReady;
        },
    },
    modules: {},
});
